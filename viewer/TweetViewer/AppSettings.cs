using System.IO;
using System.Text.Json;

namespace TweetViewer;

/// <summary>
/// %APPDATA%\Subako\settings.json。RepoDir 未設定時は exe 位置から
/// main.py を持つフォルダを探索する (exe 自身のフォルダから祖先へ順に見る)。
/// これが効くのは 2 つの配置: 開発中 (viewer/TweetViewer/bin/... 配下で動くため
/// 祖先にリポジトリがある) と、公開ビルド (fetcher 一式を exe と同じフォルダに
/// 同梱する方針 — docs/release-plan.md §2-1。この場合は最初の判定で一致する)。
/// どちらでもなければ空のままとなり、閲覧専用モードで動く。
/// </summary>
public sealed class AppSettings
{
    public string RepoDir { get; set; } = "";
    public string PythonPath { get; set; } = "python";

    /// <summary>データフォルダの場所。空なら RepoDir\data (従来動作)。</summary>
    public string DataDir { get; set; } = "";

    /// <summary>前回終了時のウィンドウ配置 (通常状態の値。null = 未保存で既定動作)。</summary>
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }

    /// <summary>前回終了時の「未読のみ」フィルタの状態。</summary>
    public bool UnreadOnly { get; set; }

    /// <summary>前回終了時のサイドバー幅 (null = 未保存で既定 260)。</summary>
    public double? SidebarWidth { get; set; }

    /// <summary>前回終了時のタグフィルタ。null = すべて表示、-1 = タグなし、それ以外 = tag_id。</summary>
    public long? TagFilterId { get; set; }

    /// <summary>設定ファイルの場所 (エラーメッセージでの案内用に公開)。</summary>
    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppInfo.Name, "settings.json");

    /// <summary>旧名 (TweetViewer) 時代の設定ファイル。初回起動時に移行する。</summary>
    private static string LegacySettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TweetViewer", "settings.json");

    [System.Text.Json.Serialization.JsonIgnore]
    public string EffectiveDataDir =>
        string.IsNullOrWhiteSpace(DataDir) ? Path.Combine(RepoDir, "data") : DataDir;

    public static AppSettings Load()
    {
        MigrateLegacySettings();
        AppSettings settings = new();
        try
        {
            if (File.Exists(SettingsPath))
                settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? settings;
        }
        catch (Exception)
        {
            // 壊れた設定は既定値で continue
        }

        if (string.IsNullOrWhiteSpace(settings.RepoDir) || !File.Exists(Path.Combine(settings.RepoDir, "main.py")))
        {
            var detected = DetectRepoDir();
            if (detected is not null)
                settings.RepoDir = detected;
        }
        return settings;
    }

    /// <summary>
    /// アプリ名変更 (TweetViewer → Subako) に伴う設定の引き継ぎ。コピーであって移動では
    /// ないので、旧バージョンに戻しても設定は残っている。失敗しても既定値で起動できる。
    /// </summary>
    private static void MigrateLegacySettings()
    {
        try
        {
            if (!File.Exists(SettingsPath) && File.Exists(LegacySettingsPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                File.Copy(LegacySettingsPath, SettingsPath);
            }
        }
        catch (Exception)
        {
            // 移行に失敗しても既定値で継続 (壊れた設定と同じ扱い)
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(
            this, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string? DetectRepoDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "main.py")) &&
                Directory.Exists(Path.Combine(dir.FullName, "sorsa_fetcher")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }
}
