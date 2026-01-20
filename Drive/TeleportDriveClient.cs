using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Teleport.Shared;

namespace Teleport.Drive
{
    /// <summary>
    /// Client for communicating with Teleport server
    /// Handles file upload, download, and metadata operations
    /// </summary>
    public class TeleportDriveClient
    {
        private readonly string _serverUrl;
        private readonly byte[] _key;
        private readonly HttpClient _httpClient;

        public TeleportDriveClient(string serverUrl, string accessKey)
        {
            _serverUrl = serverUrl.TrimEnd('/');
            _key = Crypto.DeriveKey(accessKey);
            _httpClient = new HttpClient();
        }

        /// <summary>
        /// Download a file from the server
        /// </summary>
        public async Task<byte[]> DownloadFileAsync(string slot, string path)
        {
            try
            {
                var request = Protocol.PackRequest(
                    Command.Download,
                    slot,
                    path,
                    0,
                    0,
                    null
                );

                var encryptedRequest = Crypto.Encrypt(request, _key);

                var content = new ByteArrayContent(encryptedRequest);
                var response = await _httpClient.PostAsync($"{_serverUrl}/api/transfer", content);

                if (!response.IsSuccessStatusCode)
                    return null;

                var encryptedData = await response.Content.ReadAsByteArrayAsync();
                var decryptedData = Crypto.Decrypt(encryptedData, _key);

                return decryptedData;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Download error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Upload a file to the server
        /// </summary>
        public async Task<bool> UploadFileAsync(string slot, string path, byte[] data)
        {
            try
            {
                var request = Protocol.PackRequest(
                    Command.Upload,
                    slot,
                    path,
                    0,
                    data.Length,
                    data
                );

                var encryptedRequest = Crypto.Encrypt(request, _key);
                var content = new ByteArrayContent(encryptedRequest);

                var response = await _httpClient.PostAsync($"{_serverUrl}/api/transfer", content);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Upload error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// List files in a slot directory
        /// </summary>
        public async Task<List<FileMetadata>> ListFilesAsync(string slot, string path)
        {
            try
            {
                var request = Protocol.PackRequest(
                    Command.List,
                    slot,
                    path,
                    0,
                    0,
                    null
                );

                var encryptedRequest = Crypto.Encrypt(request, _key);
                var content = new ByteArrayContent(encryptedRequest);

                var response = await _httpClient.PostAsync($"{_serverUrl}/api/transfer", content);

                if (!response.IsSuccessStatusCode)
                    return new List<FileMetadata>();

                var encryptedData = await response.Content.ReadAsByteArrayAsync();
                var decryptedData = Crypto.Decrypt(encryptedData, _key);

                // Parse file listing
                var entries = new List<FileMetadata>();
                // TODO: Parse response data into FileMetadata list

                return entries;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"List error: {ex.Message}");
                return new List<FileMetadata>();
            }
        }

        /// <summary>
        /// Get metadata for a file
        /// </summary>
        public async Task<FileMetadata> GetFileMetadataAsync(string slot, string path)
        {
            try
            {
                var directoryPath = System.IO.Path.GetDirectoryName(path) ?? "";
                var files = await ListFilesAsync(slot, directoryPath);
                var fileName = System.IO.Path.GetFileName(path);

                var metadata = files.FirstOrDefault(f => f.Name == fileName);
                return metadata;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GetMetadata error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Delete a file from the server
        /// </summary>
        public async Task<bool> DeleteFileAsync(string slot, string path)
        {
            try
            {
                var request = Protocol.PackRequest(
                    Command.Reset, // Using Reset to clear slot
                    slot,
                    path,
                    0,
                    0,
                    null
                );

                var encryptedRequest = Crypto.Encrypt(request, _key);
                var content = new ByteArrayContent(encryptedRequest);

                var response = await _httpClient.PostAsync($"{_serverUrl}/api/transfer", content);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Delete error: {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// File metadata for directory listings
    /// </summary>
    public class FileMetadata
    {
        public string? Name { get; set; }
        public long Size { get; set; }
        public bool IsDirectory { get; set; }
        public DateTime Created { get; set; }
        public DateTime Modified { get; set; }
        public DateTime Accessed { get; set; }
    }
}
