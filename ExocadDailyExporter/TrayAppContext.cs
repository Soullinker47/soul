using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ExocadDailyExporter.Models;
using ExocadDailyExporter.Services;

namespace ExocadDailyExporter;

public class TrayAppContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private Config _config;
    private Logger _logger;
    private ImportService _importService;
    private WatcherService _watcherService;
    private SchedulerService _schedulerService;
    private StartupService _startupService;
    private MainForm? _mainForm;
    private string? _statusMessage;
    private bool _isExiting;

    public TrayAppContext()
    {
        _config = Config.Load();
        InitializeServices();

        _notifyIcon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "ExocadDailyExporter",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        _notifyIcon.DoubleClick += (_, _) => ShowMainForm();

        _logger.Info("程序已启动");

        if (string.IsNullOrWhiteSpace(_config.ArchiveRoot))
        {
            ShowFirstRunDialog();
        }
        else
        {
            StartBackgroundServices();
        }
    }

    private void InitializeServices()
    {
        _watcherService?.Dispose();
        _importService?.Dispose();
        if (_logger != null)
        {
            _logger.MessageLogged -= OnLogMessage;
        }

        _logger = new Logger(_config.KeepLogDays);
        _logger.MessageLogged += OnLogMessage;
        _importService = new ImportService(_config, _logger);
        _watcherService = new WatcherService(_config, _importService, _logger);
        _schedulerService = new SchedulerService(_config, _logger);
        _startupService = new StartupService(_config, _schedulerService, _logger);
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("设置...", null, (_, _) => ShowMainForm());
        menu.Items.Add("立刻导入今天", null, async (_, _) => await RunFullImportAsync());
        menu.Items.Add("打开归档文件夹", null, (_, _) => OpenDirectory(_config.ArchiveRoot));
        menu.Items.Add("打开日志文件夹", null, (_, _) => OpenDirectory(_logger.LogDirectory));
        menu.Items.Add("退出", null, (_, _) => ExitApplication());
        return menu;
    }

    private void ShowFirstRunDialog()
    {
        using var form = new FirstRunForm(_config.ArchiveRoot);
        if (form.ShowDialog() == DialogResult.OK)
        {
            if (string.IsNullOrWhiteSpace(form.ArchiveRoot))
            {
                ExitApplication();
                return;
            }

            _config.ArchiveRoot = form.ArchiveRoot;
            _config.Save();
            InitializeServices();
            StartBackgroundServices();

            if (form.Result == FirstRunResult.StartNow)
            {
                _ = RunFullImportAsync();
            }
        }
        else
        {
            ExitApplication();
        }
    }

    private void StartBackgroundServices()
    {
        if (string.IsNullOrWhiteSpace(_config.ArchiveRoot))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(_config.ArchiveRoot);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "创建归档目录失败");
        }

        _watcherService.Start();

        var schedulerOk = _schedulerService.EnsureDailyTask();
        if (!schedulerOk && !string.IsNullOrWhiteSpace(_schedulerService.LastError))
        {
            _logger.Warn($"计划任务配置失败：{_schedulerService.LastError}");
            _statusMessage = $"计划任务失败：{_schedulerService.LastError}";
        }
        else
        {
            _statusMessage = null;
        }

        if (!_startupService.ApplyStartupSetting() && !string.IsNullOrWhiteSpace(_startupService.LastError))
        {
            _logger.Warn($"自启配置失败：{_startupService.LastError}");
            _statusMessage = string.IsNullOrEmpty(_statusMessage)
                ? $"自启配置失败：{_startupService.LastError}"
                : _statusMessage + Environment.NewLine + $"自启配置失败：{_startupService.LastError}";
        }
        else if (!string.IsNullOrWhiteSpace(_startupService.FallbackMessage))
        {
            _logger.Warn(_startupService.FallbackMessage);
            _statusMessage = string.IsNullOrEmpty(_statusMessage)
                ? _startupService.FallbackMessage
                : _statusMessage + Environment.NewLine + _startupService.FallbackMessage;
        }

        UpdateMainFormConfig();
        UpdateMainFormStatus();
        ShowBalloon("ExocadDailyExporter 已在后台运行。", ToolTipIcon.Info);
    }

    private Icon LoadIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
        if (File.Exists(iconPath))
        {
            try
            {
                return new Icon(iconPath);
            }
            catch
            {
                // ignore
            }
        }

        return SystemIcons.Application;
    }

    private void ShowMainForm()
    {
        if (_mainForm == null || _mainForm.IsDisposed)
        {
            _mainForm = new MainForm(_config);
            _mainForm.SaveRequested += OnSaveRequested;
            _mainForm.ImportTodayRequested += async (_, _) => await RunFullImportAsync();
            _mainForm.OpenArchiveRequested += (_, _) => OpenDirectory(_config.ArchiveRoot);
            _mainForm.OpenLogsRequested += (_, _) => OpenDirectory(_logger.LogDirectory);
            _mainForm.FormClosing += OnMainFormClosing;
            foreach (var message in _logger.GetRecentMessages())
            {
                _mainForm.AppendLog(message);
            }
            UpdateMainFormStatus();
        }

        _mainForm.Show();
        _mainForm.WindowState = FormWindowState.Normal;
        _mainForm.Activate();
    }

    private void OnMainFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            _mainForm?.Hide();
        }
    }

    private async Task RunFullImportAsync()
    {
        if (_importService == null)
        {
            return;
        }

        try
        {
            _logger.Info("开始执行今日全量扫描...");
            var count = await _importService.ImportTodayAsync(CancellationToken.None).ConfigureAwait(false);
            _logger.Info($"今日全量扫描完成，共处理 {count} 个病例。");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "今日全量扫描失败");
        }
    }

    private void OnSaveRequested(object? sender, Config newConfig)
    {
        if (string.IsNullOrWhiteSpace(newConfig.ArchiveRoot))
        {
            MessageBox.Show(_mainForm, "归档路径不能为空。", "设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _config = newConfig;
        _config.Save();
        InitializeServices();
        StartBackgroundServices();
        _logger.Info("配置已保存并应用");
        ShowBalloon("配置已保存并应用。", ToolTipIcon.Info);
    }

    private void UpdateMainFormConfig()
    {
        if (_mainForm != null && !_mainForm.IsDisposed)
        {
            _mainForm.LoadConfig(_config);
        }
    }

    private void UpdateMainFormStatus()
    {
        if (_mainForm != null && !_mainForm.IsDisposed)
        {
            _mainForm.SetStatusMessage(_statusMessage);
        }
    }

    private void OnLogMessage(object? sender, string message)
    {
        _mainForm?.AppendLog(message);
    }

    private void OpenDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            MessageBox.Show("尚未设置路径。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"打开目录失败：{path}");
            MessageBox.Show($"无法打开目录：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowBalloon(string text, ToolTipIcon icon)
    {
        _notifyIcon.BalloonTipText = text;
        _notifyIcon.BalloonTipIcon = icon;
        _notifyIcon.BalloonTipTitle = "ExocadDailyExporter";
        _notifyIcon.ShowBalloonTip(3000);
    }

    private void ExitApplication()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;
        ExitThread();
    }

    protected override void ExitThreadCore()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _watcherService?.Dispose();
        _importService?.Dispose();
        if (_logger != null)
        {
            _logger.MessageLogged -= OnLogMessage;
        }
        base.ExitThreadCore();
    }
}
