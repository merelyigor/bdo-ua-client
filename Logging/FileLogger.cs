using System.Text;
using System.Text.RegularExpressions;

namespace BdoClient.Logging;

public sealed class FileLogger : ILogger
{
    private const int RetentionDays = 15;
    private static readonly Regex LogFilePattern = new(@"^bdo-ua-client_(\d{4}-\d{2}-\d{2})\.log$", RegexOptions.Compiled);

    private readonly string _logsDirectory;
    private readonly object _sync = new();
    private readonly Func<DateTime>? _clock;

    public FileLogger(string logsDirectory, Func<DateTime>? clock = null)
    {
        _logsDirectory = logsDirectory ?? throw new ArgumentNullException(nameof(logsDirectory));
        _clock = clock;

        CleanupOldLogs();
    }

    public void Debug(string message) => Write("DEBUG", message);
    public void Info(string message) => Write("INFO", message);
    public void Warning(string message) => Write("WARN", message);
    public void Error(string message) => Write("ERROR", message);

    internal void CleanupOldLogs(DateTime? overrideNow = null)
    {
        try
        {
            if (!Directory.Exists(_logsDirectory))
                return;

            var now = overrideNow ?? _clock?.Invoke() ?? DateTime.Now;
            var cutoff = now.Date.AddDays(-(RetentionDays - 1));

            foreach (var file in Directory.EnumerateFiles(_logsDirectory, "bdo-ua-client_*.log"))
            {
                var fileName = Path.GetFileName(file);
                var match = LogFilePattern.Match(fileName);
                if (!match.Success)
                    continue;

                if (DateTime.TryParseExact(match.Groups[1].Value, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var fileDate))
                {
                    if (fileDate < cutoff)
                    {
                        try { File.Delete(file); }
                        catch { /* best-effort */ }
                    }
                }
            }
        }
        catch
        {
            // retention cleanup must not prevent startup
        }
    }

    private void Write(string level, string message)
    {
        try
        {
            var timestamp = DateTime.Now;
            var safeMessage = Normalize(message ?? string.Empty);

            lock (_sync)
            {
                Directory.CreateDirectory(_logsDirectory);
                var path = Path.Combine(
                    _logsDirectory,
                    $"bdo-ua-client_{timestamp:yyyy-MM-dd}.log");

                File.AppendAllText(
                    path,
                    $"{timestamp:yyyy-MM-dd HH:mm:ss.fff} [{level}] {safeMessage}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch
        {
            // logging must not throw
        }
    }

    private static string Normalize(string message)
    {
        if (string.IsNullOrEmpty(message))
            return string.Empty;

        return message
            .Replace("\r\n", " ")
            .Replace("\r", " ")
            .Replace("\n", " ");
    }
}
