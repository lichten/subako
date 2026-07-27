using System.IO;
using System.Text.Json;

namespace TweetViewer;

/// <summary>
/// %APPDATA%\TweetViewer\settings.json。RepoDir 未設定時は exe 位置から
/// main.py を持つ祖先ディレクトリを探索する(開発中は viewer/TweetViewer/bin/... 配下で動くため)。
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

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TweetViewer", "settings.json");

    [System.Text.Json.Serialization.JsonIgnore]
    public string EffectiveDataDir =>
        string.IsNullOrWhiteSpace(DataDir) ? Path.Combine(RepoDir, "data") : DataDir;

    public static AppSettings Load()
    {
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
