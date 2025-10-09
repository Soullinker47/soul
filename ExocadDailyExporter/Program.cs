using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using ExocadDailyExporter.Models;
using ExocadDailyExporter.Services;

namespace ExocadDailyExporter;

internal static class Program
{
    private const string MutexName = "Global\\ExocadDailyExporter";

    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(true, MutexName, out var created);
        if (!created)
        {
            MessageBox.Show("程序已在运行。", "ExocadDailyExporter", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (args.Any(a => string.Equals(a, "--daily-run", StringComparison.OrdinalIgnoreCase)))
        {
            RunDailyImport();
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayAppContext());
    }

    private static void RunDailyImport()
    {
        try
        {
            var config = Config.Load();
            var logger = new Logger(config.KeepLogDays);
            using var importService = new ImportService(config, logger);
            logger.Info("计划任务触发的全量扫描开始...");
            var count = importService.ImportTodayAsync(CancellationToken.None).GetAwaiter().GetResult();
            logger.Info($"计划任务触发的全量扫描完成，共处理 {count} 个病例。");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"计划任务执行失败：{ex.Message}", "ExocadDailyExporter", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

internal static class ApplicationConfiguration
{
    public static void Initialize()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
    }
}
