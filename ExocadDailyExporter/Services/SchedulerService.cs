using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using ExocadDailyExporter.Models;

namespace ExocadDailyExporter.Services;

public class SchedulerService
{
    private readonly Config _config;
    private readonly Logger _logger;
    private readonly string _dailyTaskName = "ExocadDailyExporter-Daily";
    private readonly string _startupTaskName = "ExocadDailyExporter-Startup";

    public SchedulerService(Config config, Logger logger)
    {
        _config = config;
        _logger = logger;
    }

    public string? LastError { get; private set; }

    public bool EnsureDailyTask()
    {
        if (!OperatingSystem.IsWindows())
        {
            LastError = "仅支持在 Windows 上创建计划任务";
            return false;
        }

        try
        {
            var exePath = Path.Combine(AppContext.BaseDirectory, AppDomain.CurrentDomain.FriendlyName + ".exe");
            if (!File.Exists(exePath) && Environment.ProcessPath is { } current)
            {
                exePath = current;
            }

            var dailyTime = _config.GetDailyTimeOfDay();
            var startTime = new DateTime(1, 1, 1, dailyTime.Hours, dailyTime.Minutes, 0).ToString("HH:mm");
            var command = $"{Quote(exePath)} --daily-run";
            var args = $"/Create /F /SC DAILY /TN {_dailyTaskName} /ST {startTime} /RL LIMITED /TR {Quote(command)}";
            return RunSchTasks(args);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _logger.Error(ex, "创建每日计划任务失败");
            return false;
        }
    }

    public bool RemoveDailyTask()
    {
        if (!OperatingSystem.IsWindows())
        {
            return true;
        }

        return RunSchTasks($"/Delete /TN {_dailyTaskName} /F", suppressError: true);
    }

    public bool ConfigureStartupTask(bool enable)
    {
        if (!OperatingSystem.IsWindows())
        {
            LastError = "仅支持在 Windows 上配置自启";
            return false;
        }

        try
        {
            if (enable)
            {
                var exePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, AppDomain.CurrentDomain.FriendlyName + ".exe");
                var args = $"/Create /F /SC ONLOGON /TN {_startupTaskName} /RL LIMITED /TR {Quote(exePath)}";
                return RunSchTasks(args);
            }
            else
            {
                return RunSchTasks($"/Delete /TN {_startupTaskName} /F", suppressError: true);
            }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _logger.Error(ex, "配置自启任务失败");
            return false;
        }
    }

    private bool RunSchTasks(string arguments, bool suppressError = false)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "schtasks",
                    Arguments = arguments,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    StandardErrorEncoding = Encoding.UTF8,
                    StandardOutputEncoding = Encoding.UTF8
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                if (suppressError)
                {
                    return true;
                }

                LastError = string.IsNullOrWhiteSpace(error) ? output : error;
                _logger.Warn($"schtasks 返回码 {process.ExitCode}：{LastError}");
                return false;
            }

            _logger.Info($"schtasks 执行成功：{arguments}");
            return true;
        }
        catch (Exception ex)
        {
            if (!suppressError)
            {
                LastError = ex.Message;
                _logger.Error(ex, "运行 schtasks 失败");
            }

            return false;
        }
    }

    private static string Quote(string value) => $"\"{value}\"";
}
