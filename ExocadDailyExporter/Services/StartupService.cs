using System;
using System.IO;
using System.Runtime.InteropServices;
using ExocadDailyExporter.Models;

namespace ExocadDailyExporter.Services;

public class StartupService
{
    private readonly SchedulerService _schedulerService;
    private readonly Config _config;
    private readonly Logger _logger;
    private readonly string _shortcutName = "ExocadDailyExporter.lnk";

    public StartupService(Config config, SchedulerService schedulerService, Logger logger)
    {
        _config = config;
        _schedulerService = schedulerService;
        _logger = logger;
    }

    public string? LastError { get; private set; }

    public string? FallbackMessage { get; private set; }

    public bool ApplyStartupSetting()
    {
        FallbackMessage = null;
        LastError = null;

        var enable = _config.RunAtStartup;
        if (!OperatingSystem.IsWindows())
        {
            LastError = "当前系统不支持开机自启配置";
            return false;
        }

        if (_schedulerService.ConfigureStartupTask(enable))
        {
            RemoveStartupFolderShortcut();
            return true;
        }

        LastError = _schedulerService.LastError;
        if (enable)
        {
            if (TryConfigureStartupFolder())
            {
                FallbackMessage = "计划任务创建失败，已改用启动文件夹自启。";
                return true;
            }
        }
        else
        {
            RemoveStartupFolderShortcut();
            return true;
        }

        return false;
    }

    private bool TryConfigureStartupFolder()
    {
        try
        {
            var startupPath = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            if (string.IsNullOrWhiteSpace(startupPath))
            {
                return false;
            }

            Directory.CreateDirectory(startupPath);
            var shortcutPath = Path.Combine(startupPath, _shortcutName);
            CreateShortcut(shortcutPath);
            _logger.Info($"已创建启动文件夹快捷方式：{shortcutPath}");
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _logger.Error(ex, "创建启动文件夹快捷方式失败");
            return false;
        }
    }

    private void RemoveStartupFolderShortcut()
    {
        try
        {
            var startupPath = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            var shortcutPath = Path.Combine(startupPath, _shortcutName);
            if (File.Exists(shortcutPath))
            {
                File.Delete(shortcutPath);
                _logger.Info("已移除启动文件夹快捷方式");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "删除启动文件夹快捷方式失败");
        }
    }

    private void CreateShortcut(string shortcutPath)
    {
        var target = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, AppDomain.CurrentDomain.FriendlyName + ".exe");
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null)
        {
            throw new InvalidOperationException("无法访问 WScript.Shell");
        }

        dynamic shell = Activator.CreateInstance(shellType) ?? throw new InvalidOperationException("创建 WScript.Shell 实例失败");
        try
        {
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = target;
            shortcut.WorkingDirectory = Path.GetDirectoryName(target);
            shortcut.IconLocation = target;
            shortcut.Save();
        }
        finally
        {
            if (shell is not null && Marshal.IsComObject(shell))
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
    }
}
