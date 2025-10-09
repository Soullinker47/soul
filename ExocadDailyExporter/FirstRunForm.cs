using System;
using System.IO;
using System.Windows.Forms;

namespace ExocadDailyExporter;

public enum FirstRunResult
{
    Cancel,
    SaveOnly,
    StartNow
}

public class FirstRunForm : Form
{
    private readonly TextBox _txtArchiveRoot = new();
    private readonly Button _btnBrowse = new();
    private readonly Button _btnStart = new();
    private readonly Button _btnSave = new();

    public FirstRunForm(string? archiveRoot)
    {
        Text = "设置归档路径";
        Width = 520;
        Height = 180;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var label = new Label
        {
            Text = "请选择用于存放归档文件的目标文件夹：",
            Dock = DockStyle.Top,
            Height = 30
        };

        _txtArchiveRoot.Text = archiveRoot ?? string.Empty;
        _txtArchiveRoot.Dock = DockStyle.Fill;

        _btnBrowse.Text = "浏览...";
        _btnBrowse.Width = 80;
        _btnBrowse.Click += (_, _) => BrowseArchive();

        _btnStart.Text = "开始运行";
        _btnStart.Dock = DockStyle.Fill;
        _btnStart.Height = 40;
        _btnStart.Click += (_, _) => OnStartClicked();

        _btnSave.Text = "仅保存";
        _btnSave.Dock = DockStyle.Fill;
        _btnSave.Height = 40;
        _btnSave.Click += (_, _) => OnSaveClicked();

        var pathPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            Height = 35
        };
        pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        pathPanel.Controls.Add(_txtArchiveRoot, 0, 0);
        pathPanel.Controls.Add(_btnBrowse, 1, 0);

        var buttonPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            Height = 50,
            Padding = new Padding(0, 10, 0, 0)
        };
        buttonPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        buttonPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        buttonPanel.Controls.Add(_btnStart, 0, 0);
        buttonPanel.Controls.Add(_btnSave, 1, 0);

        Controls.Add(buttonPanel);
        Controls.Add(pathPanel);
        Controls.Add(label);

        AcceptButton = _btnStart;
    }

    public string ArchiveRoot => _txtArchiveRoot.Text.Trim();

    public FirstRunResult Result { get; private set; } = FirstRunResult.Cancel;

    private void BrowseArchive()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择归档文件夹"
        };

        if (Directory.Exists(ArchiveRoot))
        {
            dialog.SelectedPath = ArchiveRoot;
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _txtArchiveRoot.Text = dialog.SelectedPath;
        }
    }

    private void OnStartClicked()
    {
        if (string.IsNullOrWhiteSpace(ArchiveRoot))
        {
            MessageBox.Show(this, "请先选择归档路径。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Result = FirstRunResult.StartNow;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void OnSaveClicked()
    {
        if (string.IsNullOrWhiteSpace(ArchiveRoot))
        {
            MessageBox.Show(this, "请先选择归档路径。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Result = FirstRunResult.SaveOnly;
        DialogResult = DialogResult.OK;
        Close();
    }
}
