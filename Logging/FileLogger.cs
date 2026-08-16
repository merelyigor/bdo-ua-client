using System.Text;

namespace BdoClient.Logging;

public sealed class FileLogger : ILogger
{
    private readonly string _logsDirectory;
    private readonly object _sync = new();

    public FileLogger(string logsDirectory)
    {
        _logsDirectory = logsDirectory ?? throw new ArgumentNullException(nameof(logsDirectory));
    }

    public void Debug(string message) => Write("DEBUG", message);
    public void Info(string message) => Write("INFO", message);
    public void Warning(string message) => Write("WARN", message);
    public void Error(string message) => Write("ERROR", message);

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
