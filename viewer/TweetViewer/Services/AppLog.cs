using System.IO;

namespace TweetViewer.Services;

/// <summary>
/// %APPDATA%\Subako\logs への簡易ファイルログ。クラッシュ時の調査材料を残すためのもので、
/// 通常運転では起動・終了と未処理例外だけを書く (ログ肥大と I/O 負荷を避ける)。
/// 不具合報告には「ログフォルダの中身を添付してください」と案内できる。
/// </summary>
public sealed class AppLog
{
    /// <summary>残す日次ログの数。それより古いものは起動時に削除する。</summary>
    private const int KeepFiles = 7;

    private readonly string _directory;
    private readonly object _gate = new();

    public AppLog(string directory) => _directory = directory;

    public static AppLog CreateDefault() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppInfo.Name, "logs"));

    public string LogDirectory => _directory;

    /// <summary>日付ごとに 1 ファイル。</summary>
    public string CurrentLogPath =>
        Path.Combine(_directory, $"{DateTime.Now:yyyyMMdd}.log");

    public void Write(string message)
    {
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(_directory);
                File.AppendAllText(
                    CurrentLogPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch (Exception)
        {
            // ログが書けない状況 (ディスク満杯等) でアプリ本体を巻き込まない
        }
    }

    public void WriteException(string context, Exception exception) =>
        Write($"[{context}] {exception}");

    /// <summary>古い日次ログを削除して直近 KeepFiles 個だけ残す。</summary>
    public void CleanupOldLogs()
    {
        try
        {
            if (!Directory.Exists(_directory))
                return;
            // ファイル名が yyyyMMdd.log なので名前の降順 = 新しい順
            foreach (var path in Directory.GetFiles(_directory, "*.log")
                         .OrderByDescending(Path.GetFileName)
                         .Skip(KeepFiles))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // 掃除の失敗は無視 (次回起動で再試行される)
        }
    }
}
