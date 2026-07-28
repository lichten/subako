using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace TweetViewer.Views;

public partial class SettingsDialog : Window
{
    private readonly AppSettings _settings;
    private readonly string _originalDataDir;

    public SettingsDialog(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        _originalDataDir = settings.EffectiveDataDir;
        DataDirBox.Text = settings.DataDir;
        RepoDirBox.Text = settings.RepoDir;
        PythonPathBox.Text = settings.PythonPath;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "データフォルダを選択",
            InitialDirectory = Directory.Exists(DataDirBox.Text.Trim())
                ? DataDirBox.Text.Trim()
                : _originalDataDir,
        };
        if (dialog.ShowDialog(this) == true)
            DataDirBox.Text = dialog.FolderName;
    }

    private void BrowseRepo_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "fetcher (main.py のあるフォルダ) を選択" };
        if (Directory.Exists(RepoDirBox.Text.Trim()))
            dialog.InitialDirectory = RepoDirBox.Text.Trim();
        if (dialog.ShowDialog(this) == true)
            RepoDirBox.Text = dialog.FolderName;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var dataDir = DataDirBox.Text.Trim();
        if (dataDir.Length > 0 && !Directory.Exists(dataDir))
        {
            MessageBox.Show("指定されたデータフォルダが存在しません。",
                AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var repoDir = RepoDirBox.Text.Trim();
        if (repoDir.Length > 0 && !File.Exists(Path.Combine(repoDir, "main.py")))
        {
            MessageBox.Show("指定されたフォルダに main.py が見つかりません。",
                AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (dataDir.Length == 0 && repoDir.Length == 0)
        {
            // EffectiveDataDir が「fetcher フォルダ内の data」に頼れなくなるため両方空は不可
            MessageBox.Show("データフォルダか fetcher の場所のどちらかは指定が必要です。",
                AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _settings.DataDir = dataDir;
        _settings.RepoDir = repoDir;
        _settings.PythonPath = PythonPathBox.Text.Trim() is { Length: > 0 } py ? py : "python";
        _settings.Save();

        if (!string.Equals(_settings.EffectiveDataDir, _originalDataDir, StringComparison.OrdinalIgnoreCase))
        {
            var restart = MessageBox.Show(
                "データフォルダの変更は再起動後に反映されます。今すぐ再起動しますか?",
                AppInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (restart == MessageBoxResult.Yes && Environment.ProcessPath is { } exe)
            {
                Process.Start(exe);
                DialogResult = true;
                Application.Current.Shutdown();
                return;
            }
        }
        DialogResult = true;
    }
}
