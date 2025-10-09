using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using ExocadDailyExporter.Models;

namespace ExocadDailyExporter.Services;

public class WatcherService : IDisposable
{
    private readonly Config _config;
    private readonly ImportService _importService;
    private readonly Logger _logger;
    private readonly ConcurrentDictionary<string, DateTime> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer _timer;
    private FileSystemWatcher? _watcher;
    private bool _disposed;

    public WatcherService(Config config, ImportService importService, Logger logger)
    {
        _config = config;
        _importService = importService;
        _logger = logger;
        _timer = new Timer(ProcessQueue, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        Stop();

        if (string.IsNullOrWhiteSpace(_config.SourceRoot) || !Directory.Exists(_config.SourceRoot))
        {
            _logger.Warn($"未找到源目录：{_config.SourceRoot}");
            return;
        }

        _watcher = new FileSystemWatcher(_config.SourceRoot)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
            Filter = "*"
        };

        _watcher.Created += OnChanged;
        _watcher.Changed += OnChanged;
        _watcher.Renamed += OnRenamed;
        _watcher.EnableRaisingEvents = true;

        var interval = Math.Max(5, _config.StabilizeSeconds / 2);
        _timer.Change(TimeSpan.FromSeconds(interval), TimeSpan.FromSeconds(interval));
        _logger.Info("文件监视服务已启动");
    }

    public void Stop()
    {
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= OnChanged;
            _watcher.Changed -= OnChanged;
            _watcher.Renamed -= OnRenamed;
            _watcher.Dispose();
            _watcher = null;
        }

        _pending.Clear();
        _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        if (_importService.IsAllowedFile(e.FullPath))
        {
            _pending[e.FullPath] = DateTime.Now.AddSeconds(_config.StabilizeSeconds);
        }
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.OldFullPath))
        {
            _pending.TryRemove(e.OldFullPath, out _);
        }

        if (_importService.IsAllowedFile(e.FullPath))
        {
            _pending[e.FullPath] = DateTime.Now.AddSeconds(_config.StabilizeSeconds);
        }
    }

    private void ProcessQueue(object? state)
    {
        if (_pending.IsEmpty)
        {
            return;
        }

        var now = DateTime.Now;
        foreach (var kvp in _pending.ToArray())
        {
            if (kvp.Value <= now && _pending.TryRemove(kvp.Key, out _))
            {
                _ = _importService.ImportCaseFromFileAsync(kvp.Key, _importService.Token);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        _timer.Dispose();
    }
}
