using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace EquipmentTracking.App.Infrastructure;

public sealed class FileLogger
{
    private const long MaximumLogBytes = 10 * 1024 * 1024;
    private static readonly string ApplicationVersion =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown";
    private readonly AppPaths _paths;
    private readonly object _syncRoot = new();
    private DateOnly _lastRetentionCheck;
    private int _retentionDays = 30;

    public FileLogger(AppPaths paths)
    {
        _paths = paths;
    }

    public void ConfigureRetention(int retentionDays)
    {
        _retentionDays = Math.Clamp(retentionDays, 1, 3650);
        _lastRetentionCheck = default;
    }

    public void Information(string message) => Write("INFO", message, null);

    public void Warning(string message) => Write("WARN", message, null);

    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    public void Workflow(
        string transactionId,
        string operation,
        string stage,
        string message,
        Exception? exception = null)
    {
        var prefix =
            $"Transaction={NormalizeLogValue(transactionId, 160)}; " +
            $"Operation={NormalizeLogValue(operation, 80)}; " +
            $"Stage={NormalizeLogValue(stage, 120)}; ";
        Write(exception is null ? "INFO" : "ERROR", prefix + message, exception);
    }

    private void Write(string level, string message, Exception? exception)
    {
        try
        {
            _paths.EnsureDirectories();
            lock (_syncRoot)
            {
                ApplyRetentionPolicy();
                var path = ResolveCurrentLogPath();
                var builder = new StringBuilder()
                    .Append(DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture))
                    .Append(" [")
                    .Append(NormalizeLogValue(level, 16))
                    .Append("] [App ")
                    .Append(ApplicationVersion)
                    .Append("] [PID ")
                    .Append(Environment.ProcessId.ToString(CultureInfo.InvariantCulture))
                    .Append("] ")
                    .Append(NormalizeLogValue(message, 8192));

                if (exception is not null)
                {
                    builder.AppendLine()
                        .Append("Exception: ")
                        .Append(NormalizeMultilineLogValue(exception.ToString(), 32768));

                    if (exception.HResult != 0)
                    {
                        builder.AppendLine()
                            .Append("HRESULT: 0x")
                            .Append(exception.HResult.ToString("X8", CultureInfo.InvariantCulture));
                    }
                }

                builder.AppendLine();
                File.AppendAllText(path, builder.ToString(), new UTF8Encoding(false));
            }
        }
        catch
        {
            // Logging must never crash the application.
        }
    }

    private string ResolveCurrentLogPath()
    {
        var datePart = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var basePath = Path.Combine(_paths.LogDirectory, $"equipment-tracking-{datePart}.log");
        if (!File.Exists(basePath) || new FileInfo(basePath).Length < MaximumLogBytes)
        {
            return basePath;
        }

        for (var index = 2; index <= 100; index++)
        {
            var candidate = Path.Combine(
                _paths.LogDirectory,
                $"equipment-tracking-{datePart}-{index:D2}.log");
            if (!File.Exists(candidate) || new FileInfo(candidate).Length < MaximumLogBytes)
            {
                return candidate;
            }
        }

        return Path.Combine(_paths.LogDirectory, $"equipment-tracking-{datePart}-overflow.log");
    }

    private void ApplyRetentionPolicy()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (_lastRetentionCheck == today)
        {
            return;
        }

        _lastRetentionCheck = today;
        var cutoff = DateTime.UtcNow.AddDays(-_retentionDays);
        foreach (var path in Directory.EnumerateFiles(_paths.LogDirectory, "equipment-tracking-*.log"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(path) < cutoff)
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Retention failure must not block normal application logging.
            }
        }
    }

    private static string NormalizeLogValue(string? value, int maximumLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(Math.Min(value.Length, maximumLength));
        foreach (var character in value)
        {
            if (builder.Length >= maximumLength)
            {
                break;
            }

            builder.Append(char.IsControl(character) ? ' ' : character);
        }

        return builder.ToString().Trim();
    }

    private static string NormalizeMultilineLogValue(string? value, int maximumLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var normalized = value.Replace("\0", string.Empty, StringComparison.Ordinal);
        return normalized.Length <= maximumLength
            ? normalized.Trim()
            : normalized[..maximumLength].Trim() + Environment.NewLine + "[truncated]";
    }
}
