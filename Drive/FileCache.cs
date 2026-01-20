using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Teleport.Drive
{
    /// <summary>
    /// File caching layer for improving performance
    /// Caches files locally to avoid repeated downloads
    /// </summary>
    public class FileCache
    {
        private readonly string _cachePath;
        private readonly Dictionary<string, CacheEntry> _metadata = new();
        private readonly object _lockObject = new();

        public FileCache(string cachePath)
        {
            _cachePath = cachePath;
            if (!Directory.Exists(_cachePath))
                Directory.CreateDirectory(_cachePath);
        }

        /// <summary>
        /// Get file from cache or download using provided function
        /// </summary>
        public byte[] GetOrDownload(string path, Func<Task<byte[]>> downloadFunc)
        {
            lock (_lockObject)
            {
                var cacheKey = GetCacheKey(path);
                var cachePath = Path.Combine(_cachePath, cacheKey);

                // Check if cached and not invalidated
                if (_metadata.TryGetValue(path, out var entry) && entry.IsValid && File.Exists(cachePath))
                {
                    entry.LastAccessTime = DateTime.UtcNow;
                    return File.ReadAllBytes(cachePath);
                }

                // Download
                try
                {
                    var data = downloadFunc().Result;

                    if (data != null)
                    {
                        // Ensure directory exists
                        var dir = Path.GetDirectoryName(cachePath);
                        if (!Directory.Exists(dir))
                            Directory.CreateDirectory(dir);

                        // Cache the file
                        File.WriteAllBytes(cachePath, data);

                        _metadata[path] = new CacheEntry
                        {
                            CachePath = cachePath,
                            CreatedTime = DateTime.UtcNow,
                            LastAccessTime = DateTime.UtcNow,
                            IsValid = true,
                            Size = data.Length
                        };

                        return data;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Cache download error: {ex.Message}");
                }

                return null;
            }
        }

        /// <summary>
        /// Invalidate a cache entry
        /// </summary>
        public void Invalidate(string path)
        {
            lock (_lockObject)
            {
                if (_metadata.TryGetValue(path, out var entry))
                {
                    entry.IsValid = false;
                    try
                    {
                        if (File.Exists(entry.CachePath))
                            File.Delete(entry.CachePath);
                    }
                    catch { }
                }
            }
        }

        /// <summary>
        /// Clean up cache directory
        /// </summary>
        public void Cleanup()
        {
            lock (_lockObject)
            {
                try
                {
                    if (Directory.Exists(_cachePath))
                        Directory.Delete(_cachePath, true);
                }
                catch { }

                _metadata.Clear();
            }
        }

        private string GetCacheKey(string path)
        {
            // Create a safe filename from the path
            var hash = System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(path)
            );
            return Convert.ToBase64String(hash).Replace('/', '_');
        }

        private class CacheEntry
        {
            public string? CachePath { get; set; }
            public long Size { get; set; }
            public DateTime CreatedTime { get; set; }
            public DateTime LastAccessTime { get; set; }
            public bool IsValid { get; set; }
        }
    }
}
