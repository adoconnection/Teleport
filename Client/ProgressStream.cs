using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Teleport.Client
{
    /// <summary>
    /// Wraps a stream to track read/write progress and update a progress bar.
    /// </summary>
    class ProgressStream : Stream
    {
        private readonly Stream _baseStream;
        private readonly StreamingProgressBar _progress;
        private long _position;

        public ProgressStream(Stream baseStream, StreamingProgressBar progress)
        {
            _baseStream = baseStream ?? throw new ArgumentNullException(nameof(baseStream));
            _progress = progress ?? throw new ArgumentNullException(nameof(progress));
            _position = 0;
        }

        public override bool CanRead => _baseStream.CanRead;
        public override bool CanSeek => _baseStream.CanSeek;
        public override bool CanWrite => _baseStream.CanWrite;
        public override long Length => _baseStream.Length;

        public override long Position
        {
            get => _position;
            set
            {
                _baseStream.Position = value;
                _position = value;
                _progress.UpdateProgress(_position);
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var bytesRead = _baseStream.Read(buffer, offset, count);
            _position += bytesRead;
            _progress.UpdateProgress(_position);
            return bytesRead;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var bytesRead = await _baseStream.ReadAsync(buffer, offset, count, cancellationToken);
            _position += bytesRead;
            _progress.UpdateProgress(_position);
            return bytesRead;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _baseStream.Write(buffer, offset, count);
            _position += count;
            _progress.UpdateProgress(_position);
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await _baseStream.WriteAsync(buffer, offset, count, cancellationToken);
            _position += count;
            _progress.UpdateProgress(_position);
        }

        public override void Flush()
        {
            _baseStream.Flush();
        }

        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            await _baseStream.FlushAsync(cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            var newPosition = _baseStream.Seek(offset, origin);
            _position = newPosition;
            _progress.UpdateProgress(_position);
            return newPosition;
        }

        public override void SetLength(long value)
        {
            _baseStream.SetLength(value);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Don't dispose the base stream - let the caller handle that
            }
            base.Dispose(disposing);
        }
    }
}
