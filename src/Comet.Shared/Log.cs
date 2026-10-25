namespace Comet.Shared
{
    using System;
    using System.IO;
    using System.Threading.Tasks;

    public enum LogLevel
    {
        Info,
        Debug,
        Warning,
        Error,
        Socket
    }

    /// <summary>
    /// Thread-safe console and rolling daily file logger shared by Account and Game servers.
    /// </summary>
    public static class Log
    {
        private static readonly object s_sync = new();

        public static string DefaultFileName { get; set; } = "Comet";

        public static Task WriteLogAsync(LogLevel level, string message)
        {
            WriteLog(level, message);
            return Task.CompletedTask;
        }

        public static Task WriteLogAsync(string folder, LogLevel level, string message)
        {
            WriteLog(level, message, folder);
            return Task.CompletedTask;
        }

        public static void WriteLog(LogLevel level, string message, string folder = "Logs")
        {
            DateTime now = DateTime.Now;
            string line = $"{now:yyyy-MM-dd HH:mm:ss} [{level}] - {message}";

            lock (s_sync)
            {
                ConsoleColor prev = Console.ForegroundColor;
                Console.ForegroundColor = level switch
                {
                    LogLevel.Info => ConsoleColor.Cyan,
                    LogLevel.Debug => ConsoleColor.Gray,
                    LogLevel.Warning => ConsoleColor.Yellow,
                    LogLevel.Error => ConsoleColor.Red,
                    LogLevel.Socket => ConsoleColor.Magenta,
                    _ => prev
                };

                Console.WriteLine(line);
                Console.ForegroundColor = prev;

                try
                {
                    Directory.CreateDirectory(folder);
                    string filePath = Path.Combine(folder, $"{DefaultFileName}-{now:yyyy-MM-dd}.log");
                    File.AppendAllText(filePath, line + Environment.NewLine);
                }
                catch
                {
                    // Ignore disk write failure so logging never disrupts server processing
                }
            }
        }
    }
}
