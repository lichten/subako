namespace TweetViewer.Data;

/// <summary>
/// viewer.db の schema_version が自分の対応バージョンより新しい (開いてはならない)。
/// 共有データフォルダを新しい版のアプリ (別 PC / Mac 版) が先に開いた場合に起きる。
/// 汎用の未処理例外として扱うとクラッシュダイアログになるため専用型にしてある
/// (docs/trending-jp.md §10.2 — Mac 版はアラート表示でアプリが生き残る)。
/// </summary>
public sealed class SchemaTooNewException : Exception
{
    public int StoredVersion { get; }
    public int SupportedVersion { get; }

    public SchemaTooNewException(int storedVersion, int supportedVersion)
        : base($"viewer.db のスキーマバージョン {storedVersion} は" +
               $"このアプリ (v{supportedVersion}) より新しいため開けません。")
    {
        StoredVersion = storedVersion;
        SupportedVersion = supportedVersion;
    }
}
