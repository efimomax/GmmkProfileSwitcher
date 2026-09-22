using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace GmmkProfileSwitcherLib
{
    /// <summary>
    /// Simple file logger that keeps only today's and yesterday's log files.
    /// Простой файловый логгер, который хранит только сегодняшний и вчерашний файлы логов.
    /// </summary>
    public static class Logger
    {
        private const string FileNamePrefix = "log-";
        private const string FileNameExtension = ".txt";
        private const string DateFormat = "yyyy-MM-dd";

        private static readonly object SyncRoot = new object();
        private static string _logDirectory;
        private static DateTime _lastCleanupDate = DateTime.MinValue;

        /// <summary>
        /// Enables or disables writing to the log file.
        /// Включает или выключает запись в файл лога.
        /// </summary>
        public static bool IsEnabled { get; set; }

        /// <summary>
        /// Directory where log files are stored. Must be set once at startup.
        /// Директория для хранения файлов логов. Должна быть задана один раз при старте.
        /// </summary>
        public static void Initialize(string logDirectory)
        {
            lock (SyncRoot)
            {
                _logDirectory = logDirectory;
            }
        }

        /// <summary>
        /// Full path of the log file for the current day.
        /// Полный путь к файлу лога за текущий день.
        /// </summary>
        public static string CurrentLogFilePath
        {
            get
            {
                if (string.IsNullOrEmpty(_logDirectory)) return null;
                return Path.Combine(_logDirectory, FileNamePrefix + DateTime.Now.ToString(DateFormat, CultureInfo.InvariantCulture) + FileNameExtension);
            }
        }

        /// <summary>
        /// Writes a message to the log file if logging is enabled.
        /// Записывает сообщение в файл лога, если логирование включено.
        /// </summary>
        public static void Log(string message)
        {
            // Always mirror to the debugger output, regardless of the setting.
            // Всегда дублируем в вывод отладчика, независимо от настройки.
            System.Diagnostics.Debug.WriteLine(message);

            if (!IsEnabled || string.IsNullOrEmpty(_logDirectory)) return;

            try
            {
                lock (SyncRoot)
                {
                    if (!Directory.Exists(_logDirectory))
                    {
                        Directory.CreateDirectory(_logDirectory);
                    }

                    CleanupOldFiles();

                    string line = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0:yyyy-MM-dd HH:mm:ss.fff} [{1}] {2}{3}",
                        DateTime.Now,
                        System.Threading.Thread.CurrentThread.ManagedThreadId,
                        message,
                        Environment.NewLine);

                    File.AppendAllText(CurrentLogFilePath, line, Encoding.UTF8);
                }
            }
            catch
            {
                // Logging must never break the application.
                // Логирование никогда не должно ломать приложение.
            }
        }

        /// <summary>
        /// Deletes log files older than yesterday. Runs at most once per day.
        /// Удаляет файлы логов старше вчерашнего дня. Выполняется не чаще раза в сутки.
        /// </summary>
        private static void CleanupOldFiles()
        {
            DateTime today = DateTime.Now.Date;
            if (_lastCleanupDate == today) return;
            _lastCleanupDate = today;

            try
            {
                // Keep only today and yesterday / Оставляем только сегодня и вчера
                var keep = new[]
                {
                    FileNamePrefix + today.ToString(DateFormat, CultureInfo.InvariantCulture) + FileNameExtension,
                    FileNamePrefix + today.AddDays(-1).ToString(DateFormat, CultureInfo.InvariantCulture) + FileNameExtension
                };

                var stale = Directory.GetFiles(_logDirectory, FileNamePrefix + "*" + FileNameExtension)
                                     .Where(f => !keep.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase));

                foreach (var file in stale)
                {
                    try { File.Delete(file); } catch { /* ignore locked files / игнорируем занятые файлы */ }
                }
            }
            catch
            {
                // Ignore cleanup failures / Игнорируем ошибки очистки
            }
        }
    }
}
