using System.Diagnostics;

namespace TweetViewer.Services;

public static class Browser
{
    /// <summary>URL を既定ブラウザで開く。失敗はダイアログ表示(呼び出し側は投げられない)。</summary>
    public static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"ブラウザを起動できませんでした: {ex.Message}",
                AppInfo.Name, System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }
}
