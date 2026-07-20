// Lightweight logger for diagnostics. Writes to Debug output and a rolling log file.
using System;
using System.Diagnostics;
using System.IO;

namespace TaskbarMiniPlayer
{
    public static class Log
    {
        private static readonly string _logPath;
        private static readonly object _lock = new();
        private const long MaxLogSizeBytes = 512 * 1024; // 500 KB

        static Log()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "TaskbarMiniPlayer");
            Directory.CreateDirectory(dir);
            _logPath = Path.Combine(dir, "taskplayer.log");
        }

        public static void Info(string message) => Write("INFO", message);
        public static void Warn(string message) => Write("WARN", message);
        public static void Error(string message, Exception? ex = null)
        {
            var msg = ex != null ? $"{message}: {ex}" : message;
            Write("ERROR", msg);
        }

        private static void Write(string level, string message)
        {
            var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
            Debug.WriteLine(entry);

            try
            {
                lock (_lock)
                {
                    // Roll the log if it gets too large
                    if (File.Exists(_logPath))
                    {
                        var info = new FileInfo(_logPath);
                        if (info.Length > MaxLogSizeBytes)
                        {
                            var backupPath = _logPath + ".old";
                            if (File.Exists(backupPath)) File.Delete(backupPath);
                            File.Move(_logPath, backupPath);
                        }
                    }

                    File.AppendAllText(_logPath, entry + Environment.NewLine);
                }
            }
            catch
            {
                // Last resort: if we can't write the log file, at least Debug output got it.
            }
        }
    }
}
