using System;

namespace Teleport.Client
{
    class StreamingProgressBar
    {
        private readonly int _totalFiles;
        private readonly long _totalBytes;
        private int _currentFileIndex;
        private long _transferredBytes;
        private string _currentFileName = "";
        private long _currentFileSize;
        private long _currentFileTransferred;
        private readonly string _operation;
        private readonly int _barWidth = 40;
        private DateTime _startTime;
        private DateTime _lastUpdate;

        public StreamingProgressBar(string operation, int totalFiles, long totalBytes)
        {
            _operation = operation;
            _totalFiles = totalFiles;
            _totalBytes = totalBytes;
            _currentFileIndex = 0;
            _transferredBytes = 0;
            _startTime = DateTime.Now;
            _lastUpdate = DateTime.MinValue;
        }

        public void StartFile(string fileName, long fileSize)
        {
            _currentFileIndex++;
            _currentFileName = fileName;
            _currentFileSize = fileSize;
            _currentFileTransferred = 0;
            Render();
        }

        public void UpdateProgress(long bytesTransferred)
        {
            _currentFileTransferred = bytesTransferred;
            var now = DateTime.Now;
            if ((now - _lastUpdate).TotalMilliseconds >= 100)
            {
                Render();
                _lastUpdate = now;
            }
        }

        public void CompleteFile()
        {
            _transferredBytes += _currentFileSize;
            _currentFileTransferred = _currentFileSize;
            Render();
        }

        public void Finish()
        {
            Console.WriteLine();
            Console.WriteLine();
        }

        private void Render()
        {
            double totalProgress;

            // If total bytes is 0 (all files are empty), calculate progress based on file count
            if (_totalBytes == 0)
            {
                totalProgress = _totalFiles > 0 ? (double)_currentFileIndex / _totalFiles : 0;
            }
            else
            {
                totalProgress = (double)(_transferredBytes + _currentFileTransferred) / _totalBytes;
            }

            totalProgress = Math.Min(totalProgress, 1.0); // Clamp to [0, 1]

            var elapsed = DateTime.Now - _startTime;
            var speed = elapsed.TotalSeconds > 0 ? (_transferredBytes + _currentFileTransferred) / elapsed.TotalSeconds : 0;

            try
            {
                Console.SetCursorPosition(0, Console.CursorTop);

                // Line 1: Operation and current file
                Console.Write($"\r{_operation} {_currentFileIndex}/{_totalFiles}: {_currentFileName}".PadRight(80));
                Console.WriteLine();

                // Line 2: Progress bar with stats
                var filled = (int)(totalProgress * _barWidth);
                var empty = _barWidth - filled;
                var bar = new string('█', filled) + new string('░', empty);

                var transferred = FormatSize(_transferredBytes + _currentFileTransferred);
                var total = FormatSize(_totalBytes);
                var speedStr = FormatSize((long)speed) + "/s";

                Console.Write($"\r{bar}  {totalProgress * 100,5:F1}%   {transferred} / {total}  {speedStr}".PadRight(80));

                Console.SetCursorPosition(0, Console.CursorTop - 1);
            }
            catch (System.IO.IOException)
            {
                // Console not available (redirected or non-interactive), just print simple progress
                Console.WriteLine($"{_operation} {_currentFileIndex}/{_totalFiles}: {_currentFileName} - {totalProgress * 100:F1}%");
            }
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }
    }
}
