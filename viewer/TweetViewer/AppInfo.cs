namespace TweetViewer;

/// <summary>
/// アプリ名の単一の定義元。ウィンドウタイトル・MessageBox キャプション・
/// %APPDATA% フォルダ名・User-Agent はすべてここを参照する
/// (名称変更時にリテラルの取りこぼしを出さないため。docs/release-plan.md §3-2)。
/// </summary>
public static class AppInfo
{
    public const string Name = "Subako";

    /// <summary>HTTP リクエストの User-Agent (例: "Subako/0.1")。</summary>
    public static string UserAgent =>
        $"{Name}/{typeof(AppInfo).Assembly.GetName().Version?.ToString(2) ?? "1.0"}";
}
