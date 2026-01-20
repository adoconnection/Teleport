using System;
using System.Collections.Generic;
using System.IO;
using System.Security.AccessControl;
using System.Threading;
using DokanNet;
using Teleport.Shared;

namespace Teleport.Drive
{
    /// <summary>
    /// Virtual file system implementation for Teleport using DokanNet
    /// Mounts a virtual drive that syncs with Teleport server
    /// </summary>
    public class TeleportVirtualDrive : IDokanOperations
    {
        private readonly TeleportDriveClient _client;
        private readonly string _slot;
        private readonly FileCache _cache;
        private readonly Dictionary<string, OpenFileHandle> _openHandles = new();
        private ulong _fileHandleCounter = 1;

        public TeleportVirtualDrive(string serverUrl, string accessKey, string slot)
        {
            _client = new TeleportDriveClient(serverUrl, accessKey);
            _slot = slot;
            _cache = new FileCache(Path.Combine(Path.GetTempPath(), "teleport-drive-cache"));
        }

        public NtStatus CreateFile(string fileName, DokanNet.FileAccess access, FileShare share, FileMode mode, FileOptions options, FileAttributes attributes, IDokanFileInfo info)
        {
            try
            {
                var path = fileName.TrimStart('\\');

                if (string.IsNullOrEmpty(path) || path == ".")
                {
                    info.IsDirectory = true;
                    return DokanResult.Success;
                }

                var isDirectory = path.EndsWith("/") || path.EndsWith("\\");
                if (isDirectory)
                {
                    info.IsDirectory = true;
                    return DokanResult.Success;
                }

                var handle = new OpenFileHandle
                {
                    Id = _fileHandleCounter++,
                    Path = path,
                    Mode = mode,
                    Access = access,
                    Data = Array.Empty<byte>()
                };

                info.Context = handle.Id;
                _openHandles[handle.Id.ToString()] = handle;

                return DokanResult.Success;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CreateFile error: {ex.Message}");
                return DokanResult.InvalidParameter;
            }
        }

        public void Cleanup(string fileName, IDokanFileInfo info)
        {
            if (info.Context != null)
            {
                _openHandles.Remove(info.Context.ToString());
            }
        }

        public void CloseFile(string fileName, IDokanFileInfo info)
        {
            Cleanup(fileName, info);
        }

        public NtStatus ReadFile(string fileName, byte[] buffer, out int bytesRead, long offset, IDokanFileInfo info)
        {
            bytesRead = 0;
            try
            {
                var path = fileName.TrimStart('\\');

                var fileData = _cache.GetOrDownload(path, async () =>
                {
                    return await _client.DownloadFileAsync(_slot, path);
                });

                if (fileData == null)
                    return DokanResult.FileNotFound;

                var bytesToCopy = Math.Min((int)(fileData.Length - offset), buffer.Length);
                if (bytesToCopy > 0)
                {
                    Array.Copy(fileData, offset, buffer, 0, bytesToCopy);
                    bytesRead = bytesToCopy;
                }

                return DokanResult.Success;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ReadFile error: {ex.Message}");
                return DokanResult.InvalidParameter;
            }
        }

        public NtStatus WriteFile(string fileName, byte[] buffer, out int bytesWritten, long offset, IDokanFileInfo info)
        {
            bytesWritten = 0;
            try
            {
                var path = fileName.TrimStart('\\');

                if (info.Context != null && _openHandles.TryGetValue(info.Context.ToString(), out var handle))
                {
                    var newData = new byte[Math.Max(handle.Data.Length, (int)(offset + buffer.Length))];
                    Array.Copy(handle.Data, newData, handle.Data.Length);
                    Array.Copy(buffer, 0, newData, (int)offset, buffer.Length);
                    handle.Data = newData;
                    bytesWritten = buffer.Length;

                    handle.IsDirty = true;
                    return DokanResult.Success;
                }

                return DokanResult.InvalidParameter;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"WriteFile error: {ex.Message}");
                return DokanResult.InvalidParameter;
            }
        }

        public NtStatus FlushFileBuffers(string fileName, IDokanFileInfo info)
        {
            try
            {
                if (info.Context != null && _openHandles.TryGetValue(info.Context.ToString(), out var handle))
                {
                    if (handle.IsDirty)
                    {
                        var path = fileName.TrimStart('\\');
                        _client.UploadFileAsync(_slot, path, handle.Data).Wait();
                        handle.IsDirty = false;
                        _cache.Invalidate(path);
                    }
                }

                return DokanResult.Success;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FlushFileBuffers error: {ex.Message}");
                return DokanResult.InvalidParameter;
            }
        }

        public NtStatus GetFileInformation(string fileName, out FileInformation fileInfo, IDokanFileInfo info)
        {
            fileInfo = new FileInformation();
            try
            {
                var path = fileName.TrimStart('\\');

                if (string.IsNullOrEmpty(path) || path == ".")
                {
                    fileInfo.Attributes = FileAttributes.Directory;
                    fileInfo.CreationTime = DateTime.Now;
                    fileInfo.LastAccessTime = DateTime.Now;
                    fileInfo.LastWriteTime = DateTime.Now;
                    fileInfo.Length = 0;
                    return DokanResult.Success;
                }

                var metadata = _client.GetFileMetadataAsync(_slot, path).Result;

                if (metadata == null)
                    return DokanResult.FileNotFound;

                fileInfo.Attributes = metadata.IsDirectory ? FileAttributes.Directory : FileAttributes.Normal;
                fileInfo.CreationTime = metadata.Created;
                fileInfo.LastAccessTime = metadata.Accessed;
                fileInfo.LastWriteTime = metadata.Modified;
                fileInfo.Length = metadata.Size;

                return DokanResult.Success;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GetFileInformation error: {ex.Message}");
                return DokanResult.FileNotFound;
            }
        }

        public NtStatus FindFiles(string fileName, out IList<FileInformation> files, IDokanFileInfo info)
        {
            files = new List<FileInformation>();
            try
            {
                var path = fileName.TrimStart('\\').TrimEnd('\\');
                if (path == ".")
                    path = "";

                var entries = _client.ListFilesAsync(_slot, path).Result;

                if (entries != null)
                {
                    foreach (var entry in entries)
                    {
                        files.Add(new FileInformation
                        {
                            Attributes = entry.IsDirectory ? FileAttributes.Directory : FileAttributes.Normal,
                            CreationTime = entry.Created,
                            LastAccessTime = entry.Accessed,
                            LastWriteTime = entry.Modified,
                            Length = entry.Size,
                            FileName = entry.Name ?? ""
                        });
                    }
                }

                return DokanResult.Success;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FindFiles error: {ex.Message}");
                return DokanResult.InvalidParameter;
            }
        }

        public NtStatus FindFilesWithPattern(string fileName, string searchPattern, out IList<FileInformation> files, IDokanFileInfo info)
        {
            return FindFiles(fileName, out files, info);
        }

        public NtStatus SetFileAttributes(string fileName, FileAttributes attributes, IDokanFileInfo info)
        {
            return DokanResult.Success;
        }

        public NtStatus SetFileTime(string fileName, DateTime? creationTime, DateTime? lastAccessTime, DateTime? lastWriteTime, IDokanFileInfo info)
        {
            return DokanResult.Success;
        }

        public NtStatus DeleteFile(string fileName, IDokanFileInfo info)
        {
            try
            {
                var path = fileName.TrimStart('\\');
                _client.DeleteFileAsync(_slot, path).Wait();
                _cache.Invalidate(path);
                return DokanResult.Success;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DeleteFile error: {ex.Message}");
                return DokanResult.InvalidParameter;
            }
        }

        public NtStatus DeleteDirectory(string fileName, IDokanFileInfo info)
        {
            return DokanResult.Success;
        }

        public NtStatus MoveFile(string oldName, string newName, bool replace, IDokanFileInfo info)
        {
            return DokanResult.NotImplemented;
        }

        public NtStatus SetEndOfFile(string fileName, long length, IDokanFileInfo info)
        {
            return DokanResult.Success;
        }

        public NtStatus SetAllocationSize(string fileName, long length, IDokanFileInfo info)
        {
            return DokanResult.Success;
        }

        public NtStatus LockFile(string fileName, long offset, long length, IDokanFileInfo info)
        {
            return DokanResult.Success;
        }

        public NtStatus UnlockFile(string fileName, long offset, long length, IDokanFileInfo info)
        {
            return DokanResult.Success;
        }

        public NtStatus GetDiskFreeSpace(out long freeBytesAvailable, out long totalNumberOfBytes, out long totalNumberOfFreeBytes, IDokanFileInfo info)
        {
            freeBytesAvailable = 1_000_000_000_000;
            totalNumberOfBytes = 10_000_000_000_000;
            totalNumberOfFreeBytes = 1_000_000_000_000;
            return DokanResult.Success;
        }

        public NtStatus GetVolumeInformation(out string volumeLabel, out FileSystemFeatures features, out string fileSystemName, out uint maximumComponentLength, IDokanFileInfo info)
        {
            volumeLabel = "Teleport";
            features = FileSystemFeatures.CasePreservedNames | FileSystemFeatures.CaseSensitiveSearch | FileSystemFeatures.SupportsRemoteStorage;
            fileSystemName = "Teleport";
            maximumComponentLength = 255;
            return DokanResult.Success;
        }

        public NtStatus GetFileSecurity(string fileName, out FileSystemSecurity? security, AccessControlSections sections, IDokanFileInfo info)
        {
            security = null;
            return DokanResult.NotImplemented;
        }

        public NtStatus SetFileSecurity(string fileName, FileSystemSecurity security, AccessControlSections sections, IDokanFileInfo info)
        {
            return DokanResult.Success;
        }

        public NtStatus Mounted(string mountPoint, IDokanFileInfo info)
        {
            Console.WriteLine("✅ Teleport virtual drive mounted successfully");
            return DokanResult.Success;
        }

        public NtStatus Unmounted(IDokanFileInfo info)
        {
            Console.WriteLine("📤 Teleport virtual drive unmounted");
            _cache.Cleanup();
            return DokanResult.Success;
        }

        public NtStatus EnumerateNamedStreams(string fileName, IntPtr enumContext, out string streamName, out long streamSize, IDokanFileInfo info)
        {
            streamName = null;
            streamSize = 0;
            return DokanResult.NotImplemented;
        }

        public NtStatus FindStreams(string fileName, out IList<FileInformation> streams, IDokanFileInfo info)
        {
            streams = new List<FileInformation>();
            return DokanResult.Success;
        }

        private class OpenFileHandle
        {
            public ulong Id { get; set; }
            public string? Path { get; set; }
            public FileMode Mode { get; set; }
            public DokanNet.FileAccess Access { get; set; }
            public byte[] Data { get; set; } = Array.Empty<byte>();
            public bool IsDirty { get; set; }
        }
    }
}
