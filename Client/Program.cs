using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.IO.Pipelines;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Teleport.Shared;
using FileEntry = Teleport.Shared.Entry;

namespace Teleport.Client
{
    class Program
    {
        private const string ConfigFileName = ".teleport-config";
        private const long ChunkSize = 1024 * 1024;

        private static byte[] _key = null!;
        private static HttpClient _httpClient = null!;
        private static string _configPath = null!;

        static async Task Main(string[] args)
        {
            Console.WriteLine("Teleport v.7");

            if (!LoadOrCreateConfig())
            {
                return;
            }

            if (args.Length < 1)
            {
                PrintUsage();
                return;
            }

            string command;
            string slotName;
            string file = null;

            if (args.Length < 2)
            {
                command = args[0].ToLower();

                if (command == "clean" || command == "clear")
                {
                    CleanLocal();
                    Console.WriteLine("Done");
                }
                else if (command == "config")
                {
                    if (File.Exists(_configPath)) File.Delete(_configPath);
                    CreateConfig();
                }
                else
                {
                    PrintUsage();
                }
                return;
            }

            slotName = args[0];
            command = args[1].ToLower();

            if (args.Length == 3)
            {
                file = args[2];
            }

            if (args.Length > 3 && args[1].ToLower() == "single")
            {
                command = args[2].ToLower();
                file = args[3];
            }

            switch (command)
            {
                case "get":
                case "down":
                case "download":
                    await Download(slotName, file);
                    break;
                case "upload":
                case "up":
                case "put":
                    await Upload(slotName, file);
                    break;
                case "clean":
                case "clear":
                    ResetSlot(slotName);
                    break;
                case "list":
                    List(slotName);
                    break;
                default:
                    PrintUsage();
                    break;
            }

            Console.WriteLine("Done");
        }

        static void PrintUsage()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  teleport <slot> upload [file]  - upload files to slot");
            Console.WriteLine("  teleport <slot> download [file] - download files from slot");
            Console.WriteLine("  teleport <slot> list           - list files in slot");
            Console.WriteLine("  teleport <slot> clean          - reset slot");
            Console.WriteLine("  teleport clean                 - clean local directory");
            Console.WriteLine("  teleport config                - reconfigure endpoint and key");
        }

        static string GetConfigPath()
        {
            var exePath = Environment.ProcessPath ?? AppContext.BaseDirectory;
            var isGlobalInstall = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                && (exePath.StartsWith("/bin/") || exePath.StartsWith("/usr/bin/") || exePath.StartsWith("/usr/local/bin/"));

#if DEBUG
            //isGlobalInstall |= RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
#else
                isGlobalInstall |= RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
#endif
            if (isGlobalInstall)
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                return Path.Combine(home, ConfigFileName);
            }

            return Path.Combine(Directory.GetCurrentDirectory(), ConfigFileName);
        }

        static bool LoadOrCreateConfig()
        {
            _configPath = GetConfigPath();

            if (File.Exists(_configPath))
            {
                try
                {
                    var config = File.ReadAllText(_configPath).Trim();
                    var parts = config.Split('|');
                    if (parts.Length == 2)
                    {
                        var serverUrl = parts[0].Trim();
                        var accessKey = parts[1].Trim();

                        if (!serverUrl.EndsWith("/")) serverUrl += "/";

                        _key = Crypto.DeriveKey(accessKey);
                        _httpClient = new HttpClient { BaseAddress = new Uri(serverUrl), Timeout = TimeSpan.FromMinutes(30) };
                        return true;
                    }
                }
                catch
                {
                    Console.WriteLine("Config file corrupted, please reconfigure.");
                }
            }

            return CreateConfig();
        }

        static bool CreateConfig()
        {
            Console.WriteLine("First run - configuration required.");
            Console.WriteLine("Enter endpoint and access key (format: http://server:port|accesskey):");
            Console.Write("> ");

            var input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input))
            {
                Console.WriteLine("Configuration cancelled.");
                return false;
            }

            var parts = input.Split('|');
            if (parts.Length != 2)
            {
                Console.WriteLine("Invalid format. Expected: http://server:port|accesskey");
                return false;
            }

            var serverUrl = parts[0].Trim();
            var accessKey = parts[1].Trim();

            if (string.IsNullOrWhiteSpace(serverUrl) || string.IsNullOrWhiteSpace(accessKey))
            {
                Console.WriteLine("Endpoint and access key cannot be empty.");
                return false;
            }

            if (!serverUrl.EndsWith("/")) serverUrl += "/";

            try
            {
                File.WriteAllText(_configPath, $"{serverUrl}|{accessKey}");
                Console.WriteLine($"Config saved to {_configPath}");

                _key = Crypto.DeriveKey(accessKey);
                _httpClient = new HttpClient { BaseAddress = new Uri(serverUrl), Timeout = TimeSpan.FromMinutes(30) };
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save config: {ex.Message}");
                return false;
            }
        }

        static bool ShouldSkipFile(string fileName)
        {
            var lower = fileName.ToLower();
            return lower == "teleport.exe" || lower == "teleport" || lower == ConfigFileName || fileName.StartsWith(".");
        }

        static void CleanLocal()
        {
            string currentPath = Directory.GetCurrentDirectory();

            foreach (string directory in Directory.GetDirectories(currentPath))
            {
                Console.WriteLine("deleting " + Path.GetFileName(directory));
                Directory.Delete(directory, true);
            }

            foreach (string file in Directory.GetFiles(currentPath))
            {
                var fileName = Path.GetFileName(file);
                if (ShouldSkipFile(fileName))
                {
                    Console.WriteLine("skipping " + fileName);
                    continue;
                }

                Console.WriteLine("deleting " + fileName);
                File.Delete(file);
            }
        }

        static void ResetSlot(string slotName)
        {
            var (success, _, error) = SendRequest(Command.Reset, slotName, null, 0, 0, null);
            if (!success)
            {
                Console.WriteLine("unable to reset slot: " + error);
            }
        }

        static async Task Upload(string slotName, string singleFile)
        {
            string currentPath = Directory.GetCurrentDirectory();

            var entries = new List<FileEntry>();

            if (string.IsNullOrWhiteSpace(singleFile))
            {
                // Upload entire directory
                EnumerateLocal(currentPath, entries, currentPath);
            }
            else
            {
                // Upload single file
                singleFile = singleFile.TrimStart('.', '\\', '/');
                var filePath = Path.Combine(currentPath, singleFile);

                if (!File.Exists(filePath))
                {
                    Console.WriteLine("file not found");
                    return;
                }

                var fileInfo = new FileInfo(filePath);
                entries.Add(new FileEntry
                {
                    Path = singleFile,
                    IsDirectory = false,
                    Size = fileInfo.Length
                });
            }

            // Filter and calculate totals
            var filesToUpload = new List<FileEntry>();
            long totalBytes = 0;

            foreach (var entry in entries)
            {
                var fileName = Path.GetFileName(entry.Path);
                if (ShouldSkipFile(fileName))
                {
                    continue;
                }

                if (!entry.IsDirectory)
                {
                    filesToUpload.Add(entry);
                    totalBytes += entry.Size;
                }
            }

            if (filesToUpload.Count == 0)
            {
                Console.WriteLine("No files to upload");
                return;
            }

            var progress = new StreamingProgressBar("Upload", filesToUpload.Count, totalBytes);
            Console.WriteLine();

            try
            {
                var pipe = new Pipe();

                using var request = new HttpRequestMessage(HttpMethod.Post, "v2/upload-stream")
                {
                    Content = new StreamContent(pipe.Reader.AsStream())
                };
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

                // Лучше SendAsync, и ResponseHeadersRead чтобы не буферить ответ целиком до твоего ReadAsByteArrayAsync
                var uploadTask = _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                Exception? writeError = null;

                try
                {
                    // leaveOpen: true — чтобы управление закрытием было у тебя через CompleteAsync
                    await using var writer = pipe.Writer.AsStream(leaveOpen: true);

                    // Протокольный префикс (как у тебя)
                    var slotNameBytes = System.Text.Encoding.UTF8.GetBytes(slotName);
                    var encryptedSlotName = Crypto.Encrypt(slotNameBytes, _key);

                    var lengthBytes = BitConverter.GetBytes(encryptedSlotName.Length); 
                    await writer.WriteAsync(lengthBytes, 0, 4);
                    await writer.WriteAsync(encryptedSlotName, 0, encryptedSlotName.Length);
                    await writer.FlushAsync();

                    // Дальше всё, что должно дойти ДО ответа сервера, обязано быть ДО await uploadTask
                    await using var encryptStream = new CryptoStreamWrapper(writer, _key, true);
                    await using var gzipStream = new GZipStream(encryptStream, CompressionLevel.Optimal, leaveOpen: false);
                    await using var tarWriter = new TarWriter(gzipStream, TarEntryFormat.Pax, leaveOpen: false);

                    foreach (var entry in filesToUpload)
                    {
                        var filePath = Path.Combine(currentPath, entry.Path);
                        var relativePath = entry.Path.Replace(Path.DirectorySeparatorChar, '/');

                        progress.StartFile(relativePath, entry.Size);

                        var tarEntry = new PaxTarEntry(TarEntryType.RegularFile, relativePath)
                        {
                            ModificationTime = File.GetLastWriteTime(filePath)
                        };

                        await using var fileStream = File.OpenRead(filePath);
                        tarEntry.DataStream = new ProgressStream(fileStream, progress);

                        await tarWriter.WriteEntryAsync(tarEntry);

                        progress.CompleteFile();
                    }

                    // КРИТИЧНО: закрыть tar/gzip/crypto, чтобы дописались хвосты
                    // await using сделает это автоматически при выходе из блока,
                    // но нам нужно сделать это ДО ожидания ответа.
                }
                catch (Exception ex)
                {
                    writeError = ex;
                }
                finally
                {
                    // Сообщаем читателю (HttpClient), что запись закончена/оборвана
                    if (writeError is null)
                        await pipe.Writer.CompleteAsync();
                    else
                        await pipe.Writer.CompleteAsync(writeError);
                }

                // Теперь уже можно ждать ответ: тело запроса точно завершено
                var response = await uploadTask;

                var responseBytes = await response.Content.ReadAsByteArrayAsync();
                var decrypted = Crypto.Decrypt(responseBytes, _key);
                var (success, _, error) = Protocol.UnpackResponse(decrypted);

                progress.Finish();

                if (writeError is not null)
                {
                    throw writeError;
                }

                if (!success)
                {
                    Console.WriteLine("\nError: " + error);
                }
            }
            catch (Exception ex)
            {
                progress.Finish();
                Console.WriteLine("\nError: " + ex.Message);
                Console.WriteLine("Stack trace: " + ex.StackTrace);
                if (ex.InnerException != null)
                {
                    Console.WriteLine("Inner: " + ex.InnerException.Message);
                }
            }
        }

        static async Task Download(string slotName, string singleFile)
        {
            // Get file list to calculate totals
            var (success, data, error) = SendRequest(Command.List, slotName, null, 0, 0, null);
            if (!success)
            {
                Console.WriteLine("unable to get file list: " + error);
                return;
            }

            var entries = Protocol.UnpackEntryList(data);

            // Calculate totals for progress bar
            var filesToDownload = new List<FileEntry>();
            long totalBytes = 0;

            foreach (var entry in entries)
            {
                var fileName = Path.GetFileName(entry.Path);
                if (ShouldSkipFile(fileName))
                {
                    continue;
                }

                if (!entry.IsDirectory)
                {
                    // If single file mode, only include the requested file
                    if (!string.IsNullOrWhiteSpace(singleFile))
                    {
                        singleFile = singleFile.TrimStart('.', '\\', '/');
                        if (entry.Path == singleFile)
                        {
                            filesToDownload.Add(entry);
                            totalBytes += entry.Size;
                            break;
                        }
                    }
                    else
                    {
                        filesToDownload.Add(entry);
                        totalBytes += entry.Size;
                    }
                }
            }

            if (filesToDownload.Count == 0)
            {
                Console.WriteLine("No files to download");
                return;
            }

            if (string.IsNullOrWhiteSpace(singleFile))
            {
                CleanLocal();
            }

            var progress = new StreamingProgressBar("Download", filesToDownload.Count, totalBytes);
            Console.WriteLine();

            try
            {
                var pipe = new Pipe();

                using var request = new HttpRequestMessage(HttpMethod.Post, "v2/download-stream")
                {
                    Content = new StreamContent(pipe.Reader.AsStream())
                };
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

                var downloadTask = _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                Exception? writeError = null;

                try
                {
                    await using var writer = pipe.Writer.AsStream(leaveOpen: true);

                    // Send encrypted slot name with length prefix
                    var slotNameBytes = System.Text.Encoding.UTF8.GetBytes(slotName);
                    var encryptedSlotName = Crypto.Encrypt(slotNameBytes, _key);

                    var lengthBytes = BitConverter.GetBytes(encryptedSlotName.Length);
                    await writer.WriteAsync(lengthBytes, 0, 4);
                    await writer.WriteAsync(encryptedSlotName, 0, encryptedSlotName.Length);

                    // Send single file name if specified
                    if (!string.IsNullOrWhiteSpace(singleFile))
                    {
                        var fileNameBytes = System.Text.Encoding.UTF8.GetBytes(singleFile);
                        var encryptedFileName = Crypto.Encrypt(fileNameBytes, _key);

                        var fileNameLengthBytes = BitConverter.GetBytes(encryptedFileName.Length);
                        await writer.WriteAsync(fileNameLengthBytes, 0, 4);
                        await writer.WriteAsync(encryptedFileName, 0, encryptedFileName.Length);
                    }
                    else
                    {
                        // Send 0 length to indicate no single file
                        var zeroBytes = BitConverter.GetBytes(0);
                        await writer.WriteAsync(zeroBytes, 0, 4);
                    }

                    await writer.FlushAsync();
                }
                catch (Exception ex)
                {
                    writeError = ex;
                }
                finally
                {
                    if (writeError is null)
                        await pipe.Writer.CompleteAsync();
                    else
                        await pipe.Writer.CompleteAsync(writeError);
                }

                // Now wait for response
                var response = await downloadTask;

                if (writeError is not null)
                {
                    throw writeError;
                }

                if (!response.IsSuccessStatusCode)
                {
                    progress.Finish();
                    Console.WriteLine($"\nError: HTTP {(int)response.StatusCode}");
                    return;
                }

                // Decrypt and extract TAR.GZ stream
                using var responseStream = await response.Content.ReadAsStreamAsync();
                await using var decryptStream = new CryptoStreamWrapper(responseStream, _key, false);
                await using var gzipStream = new GZipStream(decryptStream, CompressionMode.Decompress);
                await using var tarReader = new TarReader(gzipStream, leaveOpen: false);

                TarEntry tarEntry;

                while ((tarEntry = await tarReader.GetNextEntryAsync()) != null)
                {
                    if (tarEntry.EntryType == TarEntryType.RegularFile || tarEntry.EntryType == TarEntryType.V7RegularFile)
                    {
                        var entryPath = tarEntry.Name.Replace('/', Path.DirectorySeparatorChar);

                        // If single file mode, only extract the requested file
                        if (!string.IsNullOrWhiteSpace(singleFile))
                        {
                            var normalizedEntry = entryPath.Replace(Path.DirectorySeparatorChar, '/');
                            var normalizedSingle = singleFile.Replace(Path.DirectorySeparatorChar, '/');
                            if (normalizedEntry != normalizedSingle)
                            {
                                continue;
                            }
                        }

                        var localPath = ToLocalPath(entryPath);
                        var directory = Path.GetDirectoryName(localPath);

                        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }

                        // Find size for this entry
                        long entrySize = tarEntry.Length;
                        foreach (var e in filesToDownload)
                        {
                            if (e.Path.Replace(Path.DirectorySeparatorChar, '/') == tarEntry.Name)
                            {
                                entrySize = e.Size;
                                break;
                            }
                        }

                        progress.StartFile(entryPath, entrySize);

                        await using var fileStream = File.Create(localPath);
                        if (tarEntry.DataStream != null)
                        {
                            await using var progressStream = new ProgressStream(tarEntry.DataStream, progress);
                            await progressStream.CopyToAsync(fileStream);
                        }

                        progress.CompleteFile();
                    }
                }

                progress.Finish();
            }
            catch (Exception ex)
            {
                progress.Finish();
                Console.WriteLine("\nError: " + ex.Message);
            }
        }

        static void List(string slotName)
        {
            var (success, data, error) = SendRequest(Command.List, slotName, null, 0, 0, null);
            if (!success)
            {
                Console.WriteLine("unable to get file list: " + error);
                return;
            }

            var entries = Protocol.UnpackEntryList(data);

            foreach (var entry in entries)
            {
                if (entry.IsDirectory)
                {
                    Console.WriteLine($"[DIR]  {entry.Path}");
                }
                else
                {
                    Console.WriteLine($"       {entry.Path} ({FormatSize(entry.Size)})");
                }
            }
        }

        static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
        }

        static (bool success, byte[] data, string error) SendRequest(Command cmd, string slot, string path, long offset, long size, byte[] data)
        {
            try
            {
                var request = Protocol.PackRequest(cmd, slot, path, offset, size, data);
                var encrypted = Crypto.Encrypt(request, _key);

                using var content = new ByteArrayContent(encrypted);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

                using var response = _httpClient.PostAsync("v2/api", content).Result;

                if (!response.IsSuccessStatusCode)
                {
                    return (false, null, $"HTTP {(int)response.StatusCode}");
                }

                var responseBytes = response.Content.ReadAsByteArrayAsync().Result;
                var decrypted = Crypto.Decrypt(responseBytes, _key);

                return Protocol.UnpackResponse(decrypted);
            }
            catch (Exception ex)
            {
                return (false, null, ex.Message);
            }
        }

        static void EnumerateLocal(string basePath, IList<FileEntry> result, string activePath)
        {
            foreach (var directory in Directory.GetDirectories(activePath))
            {
                var dirName = Path.GetFileName(directory);
                if (dirName.StartsWith("."))
                {
                    continue;
                }

                result.Add(new FileEntry
                {
                    Path = ToProtocolPath(directory.Substring(basePath.Length + 1)),
                    IsDirectory = true,
                    Size = 0
                });

                EnumerateLocal(basePath, result, directory);
            }

            foreach (var file in Directory.GetFiles(activePath))
            {
                var fileName = Path.GetFileName(file);
                if (fileName.StartsWith("."))
                {
                    continue;
                }

                result.Add(new FileEntry
                {
                    Path = ToProtocolPath(file.Substring(basePath.Length + 1)),
                    IsDirectory = false,
                    Size = new FileInfo(file).Length
                });
            }
        }

        static string ToProtocolPath(string path)
        {
            return path.Replace('\\', '/');
        }

        static string ToLocalPath(string path)
        {
            return path.Replace('/', Path.DirectorySeparatorChar);
        }
    }
}
