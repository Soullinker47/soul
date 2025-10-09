using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;

namespace ExocadDailyExporter.Services;

public class Logger
{
    private readonly string _logDirectory;
    private readonly int _keepDays;
    private readonly object _cleanupLock = new();
    private readonly ConcurrentQueue<string> _recentMessages = new();

    public event EventHandler<string>? MessageLogged;

    public Logger(int keepDays)
    {
        _keepDays = keepDays;
        _logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
    }

    public string LogDirectory => _logDirectory;

    public string CurrentLogPath => Path.Combine(_logDirectory, $"archive-{DateTime.Now:yyyyMMdd}.log");

    public void Info(string message) => Write("INFO", message);

    public void Warn(string message) => Write("WARN", message);

    public void Error(string message) => Write("ERROR", message);

    public void Error(Exception ex, string message)
    {
        var text = new StringBuilder();
        text.AppendLine(message);
        text.AppendLine(ex.ToString());
        Write("ERROR", text.ToString().TrimEnd());
    }

    private void Write(string level, string message)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var line = $"[{timestamp}] [{level}] {message}";

        Directory.CreateDirectory(_logDirectory);
        File.AppendAllText(CurrentLogPath, line + Environment.NewLine, Encoding.UTF8);
        _recentMessages.Enqueue(line);
        while (_recentMessages.Count > 200 && _recentMessages.TryDequeue(out _))
        {
        }

        MessageLogged?.Invoke(this, line);
        TryCleanup();
    }

    private void TryCleanup()
    {
        lock (_cleanupLock)
        {
            if (!Directory.Exists(_logDirectory))
            {
                return;
            }

            var threshold = DateTime.Now.Date.AddDays(-_keepDays);
            foreach (var file in Directory.EnumerateFiles(_logDirectory, "archive-*.log"))
            {
                try
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    if (name?.Length >= 15)
                    {
                        var datePart = name.Substring("archive-".Length);
                        if (DateTime.TryParseExact(datePart, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                            && date < threshold)
                        {
                            File.Delete(file);
                        }
                    }
                }
                catch
                {
                    // ignore cleanup errors
                }
            }
        }
    }

    public string[] GetRecentMessages()
    {
        return _recentMessages.ToArray();
    }
}
