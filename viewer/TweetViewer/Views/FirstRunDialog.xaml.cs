using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace TweetViewer.Views;

/// <summary>
/// 初回起動 (設定が無く fetcher も検出できない) 時のセットアップ。
/// データフォルダさえ決まれば閲覧は始められるので、ここでは他の設定を求めない
/// (Python や fetcher の場所は後から設定ダイアログで構成できる)。
/// キャンセル = アプリ終了 (呼び出し側の App.OnStartup が Shutdown する)。
/// </summary>
public partial class FirstRunDialog : Window
{
    private readonly AppSettings _settings;

    public FirstRunDialog(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        Title = $"{AppInfo.Name} へようこそ";
        DataDirBox.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), AppInfo.Name);
        Loaded += (_, _) => DataDirBox.Focus();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "データフォルダを選択" };
        if (Directory.Exists(DataDirBox.Text.Trim()))
            dialog.InitialDirectory = DataDirBox.Text.Trim();
        if (dialog.ShowDialog(this) == true)
            DataDirBox.Text = dialog.FolderName;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var dataDir = DataDirBox.Text.Trim();
        if (dataDir.Length == 0)
        {
            MessageBox.Show("データフォルダを指定してください。",
                AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            Directory.CreateDirectory(dataDir);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"フォルダを作成できませんでした。\n{ex.Message}",
                AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _settings.DataDir = dataDir;
        DialogResult = true;
    }
}
