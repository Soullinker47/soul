using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using ExocadDailyExporter.Models;

namespace ExocadDailyExporter;

public class MainForm : Form
{
    private readonly TextBox _txtSourceRoot = new();
    private readonly TextBox _txtArchiveRoot = new();
    private readonly TextBox _txtDailyTime = new();
    private readonly NumericUpDown _numStabilize = new();
    private readonly NumericUpDown _numKeepLogs = new();
    private readonly CheckBox _chkRunAtStartup = new();
    private readonly Button _btnBrowseSource = new();
    private readonly Button _btnBrowseArchive = new();
    private readonly Button _btnSaveRestart = new();
    private readonly Button _btnImportToday = new();
    private readonly Button _btnOpenArchive = new();
    private readonly Button _btnOpenLogs = new();
    private readonly TextBox _txtLogs = new();
    private readonly Label _lblStatus = new();
    private readonly System.Collections.Generic.List<string> _excludePatterns;

    public event EventHandler<Config>? SaveRequested;
    public event EventHandler? ImportTodayRequested;
    public event EventHandler? OpenArchiveRequested;
    public event EventHandler? OpenLogsRequested;

    public MainForm(Config config)
    {
        _excludePatterns = config.ExcludePatterns.ToList();
        Text = "Exocad 档案导出助手";
        Width = 680;
        Height = 520;
        StartPosition = FormStartPosition.CenterScreen;

        _txtSourceRoot.Width = 400;
        _txtArchiveRoot.Width = 400;
        _txtDailyTime.Width = 80;

        _numStabilize.Minimum = 5;
        _numStabilize.Maximum = 600;
        _numStabilize.Value = config.StabilizeSeconds;

        _numKeepLogs.Minimum = 1;
        _numKeepLogs.Maximum = 365;
        _numKeepLogs.Value = config.KeepLogDays;

        _chkRunAtStartup.Text = "开机自启";
        _chkRunAtStartup.Checked = config.RunAtStartup;

        _btnBrowseSource.Text = "浏览...";
        _btnBrowseSource.Click += (_, _) => BrowseFolder(_txtSourceRoot);

        _btnBrowseArchive.Text = "浏览...";
        _btnBrowseArchive.Click += (_, _) => BrowseFolder(_txtArchiveRoot);

        _btnSaveRestart.Text = "保存并重启服务";
        _btnSaveRestart.Width = 150;
        _btnSaveRestart.Click += (_, _) => OnSaveClicked();

        _btnImportToday.Text = "立刻导入今天";
        _btnImportToday.Width = 150;
        _btnImportToday.Click += (_, _) => ImportTodayRequested?.Invoke(this, EventArgs.Empty);

        _btnOpenArchive.Text = "打开归档文件夹";
        _btnOpenArchive.Width = 150;
        _btnOpenArchive.Click += (_, _) => OpenArchiveRequested?.Invoke(this, EventArgs.Empty);

        _btnOpenLogs.Text = "打开日志文件夹";
        _btnOpenLogs.Width = 150;
        _btnOpenLogs.Click += (_, _) => OpenLogsRequested?.Invoke(this, EventArgs.Empty);

        _txtLogs.Multiline = true;
        _txtLogs.ReadOnly = true;
        _txtLogs.ScrollBars = ScrollBars.Vertical;
        _txtLogs.Dock = DockStyle.Fill;

        _lblStatus.ForeColor = Color.DarkOrange;
        _lblStatus.Dock = DockStyle.Top;
        _lblStatus.Height = 40;

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 3,
            RowCount = 6,
            AutoSize = true
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));

        AddRow(table, 0, "病例根目录", _txtSourceRoot, _btnBrowseSource);
        AddRow(table, 1, "归档路径", _txtArchiveRoot, _btnBrowseArchive);
        AddRow(table, 2, "稳定等待(秒)", _numStabilize, null);
        AddRow(table, 3, "每日全量时间", _txtDailyTime, null);
        AddRow(table, 4, "日志保留(天)", _numKeepLogs, null);
        AddRow(table, 5, string.Empty, _chkRunAtStartup, null);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            Padding = new Padding(0, 10, 0, 10)
        };
        buttonPanel.Controls.AddRange(new Control[]
        {
            _btnSaveRestart,
            _btnImportToday,
            _btnOpenArchive,
            _btnOpenLogs
        });

        var logPanel = new GroupBox
        {
            Text = "运行日志",
            Dock = DockStyle.Fill
        };
        logPanel.Controls.Add(_txtLogs);

        var container = new Panel { Dock = DockStyle.Fill };
        container.Controls.Add(logPanel);
        container.Controls.Add(buttonPanel);
        container.Controls.Add(table);
        container.Controls.Add(_lblStatus);

        Controls.Add(container);

        LoadConfig(config);
    }

    public void LoadConfig(Config config)
    {
        _txtSourceRoot.Text = config.SourceRoot;
        _txtArchiveRoot.Text = config.ArchiveRoot;
        _txtDailyTime.Text = config.DailyTime;
        _numStabilize.Value = Math.Min(_numStabilize.Maximum, Math.Max(_numStabilize.Minimum, config.StabilizeSeconds));
        _numKeepLogs.Value = Math.Min(_numKeepLogs.Maximum, Math.Max(_numKeepLogs.Minimum, config.KeepLogDays));
        _chkRunAtStartup.Checked = config.RunAtStartup;
        _excludePatterns.Clear();
        _excludePatterns.AddRange(config.ExcludePatterns);
    }

    public void AppendLog(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<string>(AppendLog), message);
            return;
        }

        _txtLogs.AppendText(message + Environment.NewLine);
        _txtLogs.SelectionStart = _txtLogs.TextLength;
        _txtLogs.ScrollToCaret();
    }

    public void SetStatusMessage(string? message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<string?>(SetStatusMessage), message);
            return;
        }

        _lblStatus.Text = message ?? string.Empty;
        _lblStatus.Visible = !string.IsNullOrEmpty(message);
    }

    private void OnSaveClicked()
    {
        var config = new Config
        {
            SourceRoot = _txtSourceRoot.Text.Trim(),
            ArchiveRoot = _txtArchiveRoot.Text.Trim(),
            StabilizeSeconds = (int)_numStabilize.Value,
            DailyTime = _txtDailyTime.Text.Trim(),
            RunAtStartup = _chkRunAtStartup.Checked,
            KeepLogDays = (int)_numKeepLogs.Value,
            ExcludePatterns = _excludePatterns.ToList()
        };

        SaveRequested?.Invoke(this, config);
    }

    private static void AddRow(TableLayoutPanel table, int row, string labelText, Control input, Control? button)
    {
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        if (!string.IsNullOrEmpty(labelText))
        {
            var label = new Label
            {
                Text = labelText,
                TextAlign = ContentAlignment.MiddleRight,
                Dock = DockStyle.Fill
            };
            table.Controls.Add(label, 0, row);
        }
        else
        {
            table.Controls.Add(new Label { Text = string.Empty }, 0, row);
        }

        input.Dock = DockStyle.Fill;
        table.Controls.Add(input, 1, row);

        if (button != null)
        {
            table.Controls.Add(button, 2, row);
        }
        else
        {
            table.Controls.Add(new Label { Text = string.Empty }, 2, row);
        }
    }

    private static void BrowseFolder(TextBox target)
    {
        using var dialog = new FolderBrowserDialog();
        if (Directory.Exists(target.Text))
        {
            dialog.SelectedPath = target.Text;
        }

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            target.Text = dialog.SelectedPath;
        }
    }
}
