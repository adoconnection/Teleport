using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Teleport.Shared
{
    /// <summary>
    /// Wraps a stream to provide chunked AES-GCM encryption/decryption for streaming scenarios.
    /// Each chunk is encrypted independently with its own nonce for security.
    /// </summary>
    public class CryptoStreamWrapper : Stream
    {
        private const int ChunkSize = 64 * 1024; // 64KB chunks
        private const int NonceSize = 12;
        private const int TagSize = 16;

        private readonly Stream _baseStream;
        private readonly byte[] _key;
        private readonly bool _encrypt;
        private readonly byte[] _buffer;
        private int _bufferPosition;
        private int _bufferLength;
        private bool _disposed;

        public CryptoStreamWrapper(Stream baseStream, byte[] key, bool encrypt)
        {
            _baseStream = baseStream ?? throw new ArgumentNullException(nameof(baseStream));
            _key = key ?? throw new ArgumentNullException(nameof(key));
            _encrypt = encrypt;
            _buffer = new byte[ChunkSize];
            _bufferPosition = 0;
            _bufferLength = 0;
        }

        public override bool CanRead => !_encrypt && _baseStream.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => _encrypt && _baseStream.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
            if (_encrypt && _bufferPosition > 0)
            {
                FlushBuffer();
            }
            _baseStream.Flush();
        }

        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            if (_encrypt && _bufferPosition > 0)
            {
                await FlushBufferAsync(cancellationToken);
            }
            await _baseStream.FlushAsync(cancellationToken);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_encrypt) throw new NotSupportedException("Cannot read from encryption stream");

            // Read and decrypt one chunk at a time
            if (_bufferPosition >= _bufferLength)
            {
                // Read chunk size (4 bytes)
                var sizeBuffer = new byte[4];
                int bytesRead = 0;
                try
                {
                    bytesRead = ReadExact(_baseStream, sizeBuffer, 0, 4);
                }
                catch (EndOfStreamException)
                {
                    // Gracefully handle end of stream
                    return 0;
                }

                if (bytesRead == 0) return 0; // End of stream

                var chunkSize = BitConverter.ToInt32(sizeBuffer, 0);
                if (chunkSize <= 0 || chunkSize > ChunkSize + NonceSize + TagSize)
                    throw new CryptographicException("Invalid chunk size");

                // Read encrypted chunk
                var encryptedChunk = new byte[chunkSize];
                ReadExact(_baseStream, encryptedChunk, 0, chunkSize);

                // Decrypt chunk
                var decrypted = Crypto.Decrypt(encryptedChunk, _key);

                // Store in buffer
                _bufferLength = decrypted.Length;
                _bufferPosition = 0;
                Buffer.BlockCopy(decrypted, 0, _buffer, 0, decrypted.Length);
            }

            // Copy from buffer to output
            var bytesToCopy = Math.Min(count, _bufferLength - _bufferPosition);
            Buffer.BlockCopy(_buffer, _bufferPosition, buffer, offset, bytesToCopy);
            _bufferPosition += bytesToCopy;

            return bytesToCopy;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (_encrypt) throw new NotSupportedException("Cannot read from encryption stream");

            // Read and decrypt one chunk at a time
            if (_bufferPosition >= _bufferLength)
            {
                // Read chunk size (4 bytes)
                var sizeBuffer = new byte[4];
                int bytesRead = 0;
                try
                {
                    bytesRead = await ReadExactAsync(_baseStream, sizeBuffer, 0, 4, cancellationToken);
                }
                catch (EndOfStreamException)
                {
                    // Gracefully handle end of stream
                    return 0;
                }

                if (bytesRead == 0) return 0; // End of stream

                var chunkSize = BitConverter.ToInt32(sizeBuffer, 0);
                if (chunkSize <= 0 || chunkSize > ChunkSize + NonceSize + TagSize)
                    throw new CryptographicException("Invalid chunk size");

                // Read encrypted chunk
                var encryptedChunk = new byte[chunkSize];
                await ReadExactAsync(_baseStream, encryptedChunk, 0, chunkSize, cancellationToken);

                // Decrypt chunk
                var decrypted = Crypto.Decrypt(encryptedChunk, _key);

                // Store in buffer
                _bufferLength = decrypted.Length;
                _bufferPosition = 0;
                Buffer.BlockCopy(decrypted, 0, _buffer, 0, decrypted.Length);
            }

            // Copy from buffer to output
            var bytesToCopy = Math.Min(count, _bufferLength - _bufferPosition);
            Buffer.BlockCopy(_buffer, _bufferPosition, buffer, offset, bytesToCopy);
            _bufferPosition += bytesToCopy;

            return bytesToCopy;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (!_encrypt) throw new NotSupportedException("Cannot write to decryption stream");

            while (count > 0)
            {
                var bytesToBuffer = Math.Min(count, ChunkSize - _bufferPosition);
                Buffer.BlockCopy(buffer, offset, _buffer, _bufferPosition, bytesToBuffer);

                _bufferPosition += bytesToBuffer;
                offset += bytesToBuffer;
                count -= bytesToBuffer;

                if (_bufferPosition >= ChunkSize)
                {
                    FlushBuffer();
                }
            }
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (!_encrypt) throw new NotSupportedException("Cannot write to decryption stream");

            while (count > 0)
            {
                var bytesToBuffer = Math.Min(count, ChunkSize - _bufferPosition);
                Buffer.BlockCopy(buffer, offset, _buffer, _bufferPosition, bytesToBuffer);

                _bufferPosition += bytesToBuffer;
                offset += bytesToBuffer;
                count -= bytesToBuffer;

                if (_bufferPosition >= ChunkSize)
                {
                    await FlushBufferAsync(cancellationToken);
                }
            }
        }

        private void FlushBuffer()
        {
            if (_bufferPosition == 0) return;

            // Encrypt the buffered data
            var plaintext = new byte[_bufferPosition];
            Buffer.BlockCopy(_buffer, 0, plaintext, 0, _bufferPosition);

            var encrypted = Crypto.Encrypt(plaintext, _key);

            // Write chunk size + encrypted data
            var sizeBuffer = BitConverter.GetBytes(encrypted.Length);
            _baseStream.Write(sizeBuffer, 0, 4);
            _baseStream.Write(encrypted, 0, encrypted.Length);

            _bufferPosition = 0;
        }

        private async Task FlushBufferAsync(CancellationToken cancellationToken)
        {
            if (_bufferPosition == 0) return;

            // Encrypt the buffered data
            var plaintext = new byte[_bufferPosition];
            Buffer.BlockCopy(_buffer, 0, plaintext, 0, _bufferPosition);

            var encrypted = Crypto.Encrypt(plaintext, _key);

            // Write chunk size + encrypted data
            var sizeBuffer = BitConverter.GetBytes(encrypted.Length);
            await _baseStream.WriteAsync(sizeBuffer, 0, 4, cancellationToken);
            await _baseStream.WriteAsync(encrypted, 0, encrypted.Length, cancellationToken);

            _bufferPosition = 0;
        }

        private static int ReadExact(Stream stream, byte[] buffer, int offset, int count)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                var bytesRead = stream.Read(buffer, offset + totalRead, count - totalRead);
                if (bytesRead == 0)
                {
                    if (totalRead == 0) return 0; // End of stream at start
                    throw new EndOfStreamException("Unexpected end of stream");
                }
                totalRead += bytesRead;
            }
            return totalRead;
        }

        private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                var bytesRead = await stream.ReadAsync(buffer, offset + totalRead, count - totalRead, cancellationToken);
                if (bytesRead == 0)
                {
                    if (totalRead == 0) return 0; // End of stream at start
                    throw new EndOfStreamException("Unexpected end of stream");
                }
                totalRead += bytesRead;
            }
            return totalRead;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    if (_encrypt)
                    {
                        Flush();
                    }
                    // Don't dispose the base stream - let the caller handle that
                }
                _disposed = true;
            }
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                if (_encrypt)
                {
                    await FlushAsync();
                }
                _disposed = true;
            }
            await base.DisposeAsync();
        }
    }
}
