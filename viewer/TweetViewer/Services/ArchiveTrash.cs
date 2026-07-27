using System.Globalization;
using System.IO;

namespace TweetViewer.Services;

/// <summary>
/// 削除時にアーカイブフォルダを退避する先 (data/_trash/)。
/// 起動時の自動登録 (UserRepository.RegisterExistingDataDirsAsync /
/// RegisterExistingSearchDirsAsync) は data/ 直下と data/searches/ 直下しか見ないため、
/// ここへ移すだけで「ファイルは残すが一覧には出さない」が成立する。
/// </summary>
public static class ArchiveTrash
{
    public const string TrashDirName = "_trash";

    public static string TrashDir(string dataDir) => Path.Combine(dataDir, TrashDirName);

    /// <summary>
    /// data/&lt;username&gt;/ を data/_trash/&lt;username&gt;/ へ移動し、移動先パスを返す。
    /// 元フォルダが無い場合は null。戻せるよう通常はサフィックスを付けず、
    /// 同名が既にある場合だけ日時を付ける。
    /// </summary>
    public static string? MoveToTrash(string dataDir, string username)
    {
        var source = Path.Combine(dataDir, username);
        if (!Directory.Exists(source))
            return null;
        var dest = Path.Combine(TrashDir(dataDir), username);
        if (Directory.Exists(dest))
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            dest = Path.Combine(TrashDir(dataDir), $"{username}_{stamp}");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        Directory.Move(source, dest);
        return dest;
    }
}
