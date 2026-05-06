using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Teleport.Shared;
using FileEntry = Teleport.Shared.Entry;

namespace Teleport.Server.Controllers
{
    [ApiController]
    public class HomeController : ControllerBase
    {
        private readonly byte[] _key;
        private readonly string _storePath;

        public HomeController(IConfiguration configuration)
        {
            var accessKey = configuration["AccessKey"] ?? throw new InvalidOperationException("AccessKey not configured");
            var storePath = configuration["StorePath"] ?? throw new InvalidOperationException("StorePath not configured");

            _key = Crypto.DeriveKey(accessKey);
            _storePath = storePath;

            if (!Directory.Exists(_storePath))
            {
                Directory.CreateDirectory(_storePath);
            }
        }

        [HttpGet("")]
        public string Index()
        {
            return "Teleport v6";
        }

        [HttpPost("v2/api")]
        [DisableRequestSizeLimit]
        [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue)]
        public async Task<IActionResult> HandleRequest()
        {
            try
            {
                using var ms = new MemoryStream();
                await Request.Body.CopyToAsync(ms);
                var encrypted = ms.ToArray();

                byte[] decrypted;
                try
                {
                    decrypted = Crypto.Decrypt(encrypted, _key);
                }
                catch
                {
                    return BadRequest("Decryption failed");
                }

                var (cmd, slot, path, offset, size, data) = Protocol.UnpackRequest(decrypted);

                if (string.IsNullOrWhiteSpace(slot))
                {
                    return EncryptedResponse(false, null, "Invalid slot name");
                }

                byte[] response = cmd switch
                {
                    Command.List => HandleList(slot),
                    Command.Reset => HandleReset(slot),
                    _ => Protocol.PackResponse(false, null, "Command not supported (use streaming endpoints)")
                };

                return File(Crypto.Encrypt(response, _key), "application/octet-stream");
            }
            catch (Exception ex)
            {
                var errorResponse = Protocol.PackResponse(false, null, ex.Message);
                return File(Crypto.Encrypt(errorResponse, _key), "application/octet-stream");
            }
        }

        private IActionResult EncryptedResponse(bool success, byte[]? data, string? error = null)
        {
            var response = Protocol.PackResponse(success, data, error);
            return File(Crypto.Encrypt(response, _key), "application/octet-stream");
        }

        private byte[] HandleList(string slot)
        {
            var slotPath = GetSlotPath(slot);

            if (!Directory.Exists(slotPath))
            {
                Directory.CreateDirectory(slotPath);
            }

            var entries = new List<FileEntry>();
            EnumerateSlot(slotPath, entries, slotPath);

            var data = Protocol.PackEntryList(entries);
            return Protocol.PackResponse(true, data);
        }

        private byte[] HandleReset(string slot)
        {
            var slotPath = GetSlotPath(slot);

            if (Directory.Exists(slotPath))
            {
                Directory.Delete(slotPath, true);
            }

            Directory.CreateDirectory(slotPath);
            return Protocol.PackResponse(true, null);
        }

        private void EnumerateSlot(string slotPath, IList<FileEntry> result, string activePath)
        {
            foreach (var directory in Directory.GetDirectories(activePath))
            {
                result.Add(new FileEntry
                {
                    Path = NormalizePath(directory.Substring(slotPath.Length + 1)),
                    IsDirectory = true,
                    Size = 0
                });

                EnumerateSlot(slotPath, result, directory);
            }

            foreach (var file in Directory.GetFiles(activePath))
            {
                result.Add(new FileEntry
                {
                    Path = NormalizePath(file.Substring(slotPath.Length + 1)),
                    IsDirectory = false,
                    Size = new FileInfo(file).Length
                });
            }
        }

        private static string NormalizePath(string path)
        {
            return path.Replace('\\', '/');
        }

        private string? NormalizeSubPath(string? relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return null;
            }

            return relativePath.Replace("..", ".").Replace("~", "").Trim('\\').Trim('/').Trim();
        }

        private string GetSlotPath(string name, string? subPath = null)
        {
            name = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));

            if (!string.IsNullOrWhiteSpace(subPath))
            {
                return Path.Combine(_storePath, name, subPath);
            }

            return Path.Combine(_storePath, name);
        }

        [HttpPost("v2/download-stream")]
        [DisableRequestSizeLimit]
        public async Task<IActionResult> DownloadStream()
        {
            // Enable synchronous IO for this request (required by SharpCompress)
            var syncIOFeature = HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpBodyControlFeature>();
            if (syncIOFeature != null)
            {
                syncIOFeature.AllowSynchronousIO = true;
            }

            try
            {
                var lengthBuffer = new byte[4];
                await Request.Body.ReadAsync(lengthBuffer, 0, 4);
                var slotNameLength = BitConverter.ToInt32(lengthBuffer, 0);

                var encryptedSlotName = new byte[slotNameLength];
                await Request.Body.ReadAsync(encryptedSlotName, 0, slotNameLength);

                byte[] decryptedSlotName;
                try
                {
                    decryptedSlotName = Crypto.Decrypt(encryptedSlotName, _key);
                }
                catch
                {
                    return BadRequest("Decryption failed");
                }

                var slotName = System.Text.Encoding.UTF8.GetString(decryptedSlotName);

                if (string.IsNullOrWhiteSpace(slotName))
                {
                    return BadRequest("Invalid slot name");
                }

                var slotPath = GetSlotPath(slotName);

                if (!Directory.Exists(slotPath))
                {
                    return NotFound("Slot not found");
                }

                var singleFileLengthBuffer = new byte[4];
                await Request.Body.ReadAsync(singleFileLengthBuffer, 0, 4);
                var singleFileNameLength = BitConverter.ToInt32(singleFileLengthBuffer, 0);

                byte[] singleFileNameBytes;
                string singleFileName = null;

                if (singleFileNameLength > 0)
                {
                    singleFileNameBytes = new byte[singleFileNameLength];
                    await Request.Body.ReadAsync(singleFileNameBytes, 0, singleFileNameLength);

                    try
                    {
                        singleFileName = System.Text.Encoding.UTF8.GetString(Crypto.Decrypt(singleFileNameBytes, _key));
                    }
                    catch
                    {
                        return BadRequest("Decryption failed");
                    }
                }


                // Create encrypted stream pipeline: TAR -> GZIP -> Encrypt -> Response
                Response.ContentType = "application/octet-stream";
                Response.Headers["X-Content-Type"] = "application/gzip";

                // Create a crypto stream wrapper
                await using var encryptStream = new CryptoStreamWrapper(Response.Body, _key, true);
                await using var gzipStream = new GZipStream(encryptStream, CompressionLevel.Optimal, leaveOpen: false);
                await using var tarWriter = new TarWriter(gzipStream, TarEntryFormat.Pax, leaveOpen: false);

                try
                {
                    if (string.IsNullOrWhiteSpace(singleFileName))
                    {
                        // Add all files from slot to TAR archive
                        await AddFilesToTarAsync(slotPath, slotPath, tarWriter);
                    }
                    else
                    {
                        // Build full path for single file
                        var normalizedFileName = singleFileName.Replace('/', Path.DirectorySeparatorChar);
                        var fullFilePath = Path.Combine(slotPath, normalizedFileName);

                        if (!System.IO.File.Exists(fullFilePath))
                        {
                            return NotFound($"File not found: {singleFileName}");
                        }

                        await AddFileToTarAsync(slotPath, fullFilePath, tarWriter);
                    }

                    return new EmptyResult();
                }
                catch (Exception ex)
                {
                    return StatusCode(500, ex.Message);
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        [HttpPost("v2/upload-stream")]
        [DisableRequestSizeLimit]
        [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue)]
        public async Task<IActionResult> UploadStream()
        {
            // Enable synchronous IO for this request (required by SharpCompress)
            var syncIOFeature = HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpBodyControlFeature>();

            if (syncIOFeature != null)
            {
                syncIOFeature.AllowSynchronousIO = true;
            }

            try
            {
                // First 4 bytes: encrypted slot name length
                // Next N bytes: encrypted slot name
                // Rest: encrypted TAR.GZ stream

                var lengthBuffer = new byte[4];
                await Request.Body.ReadAsync(lengthBuffer, 0, 4);
                var slotNameLength = BitConverter.ToInt32(lengthBuffer, 0);

                var encryptedSlotName = new byte[slotNameLength];
                await Request.Body.ReadAsync(encryptedSlotName, 0, slotNameLength);

                byte[] decryptedSlotName;
                try
                {
                    decryptedSlotName = Crypto.Decrypt(encryptedSlotName, _key);
                }
                catch
                {
                    return BadRequest("Decryption failed");
                }

                var slotName = System.Text.Encoding.UTF8.GetString(decryptedSlotName);

                if (string.IsNullOrWhiteSpace(slotName))
                {
                    return BadRequest("Invalid slot name");
                }

                var slotPath = GetSlotPath(slotName);

                // Clear existing slot
                if (Directory.Exists(slotPath))
                {
                    Directory.Delete(slotPath, true);
                }
                Directory.CreateDirectory(slotPath);

                // Decrypt and extract TAR.GZ stream
                await using var decryptStream = new CryptoStreamWrapper(Request.Body, _key, false);
                await using var gzipStream = new GZipStream(decryptStream, CompressionMode.Decompress, leaveOpen: true);
                await using var tarReader = new TarReader(gzipStream, leaveOpen: false);

                try
                {
                    TarEntry tarEntry;
                    while ((tarEntry = await tarReader.GetNextEntryAsync()) != null)
                    {
                        if (tarEntry.EntryType == TarEntryType.RegularFile || tarEntry.EntryType == TarEntryType.V7RegularFile)
                        {
                            var entryPath = tarEntry.Name.Replace('/', Path.DirectorySeparatorChar);
                            var fullPath = Path.Combine(slotPath, entryPath);
                            var directory = Path.GetDirectoryName(fullPath);

                            if (directory != null && !Directory.Exists(directory))
                            {
                                Directory.CreateDirectory(directory);
                            }

                            await using var fileStream = System.IO.File.Create(fullPath);
                            if (tarEntry.DataStream != null)
                            {
                                await tarEntry.DataStream.CopyToAsync(fileStream);
                            }
                        }
                    }

                    var response = Protocol.PackResponse(true, null);
                    return File(Crypto.Encrypt(response, _key), "application/octet-stream");
                }
                catch (Exception ex)
                {
                    var errorResponse = Protocol.PackResponse(false, null, ex.Message);
                    return File(Crypto.Encrypt(errorResponse, _key), "application/octet-stream");
                }
            }
            catch (Exception ex)
            {
                var errorResponse = Protocol.PackResponse(false, null, ex.Message);
                return File(Crypto.Encrypt(errorResponse, _key), "application/octet-stream");
            }
        }

        private async Task AddFilesToTarAsync(string slotPath, string currentPath, TarWriter tarWriter)
        {
            foreach (var file in Directory.GetFiles(currentPath))
            {
                await AddFileToTarAsync(slotPath, file, tarWriter);
            }

            foreach (var directory in Directory.GetDirectories(currentPath))
            {
                await AddFilesToTarAsync(slotPath, directory, tarWriter);
            }
        }

        private async Task AddFileToTarAsync(string slotPath, string file, TarWriter tarWriter)
        {
            var relativePath = file.Substring(slotPath.Length + 1).Replace(Path.DirectorySeparatorChar, '/');

            var tarEntry = new PaxTarEntry(TarEntryType.RegularFile, relativePath)
            {
                ModificationTime = System.IO.File.GetLastWriteTime(file)
            };

            await using var fileStream = System.IO.File.OpenRead(file);
            tarEntry.DataStream = fileStream;

            await tarWriter.WriteEntryAsync(tarEntry);

        }
    }
}
