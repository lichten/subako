using System.IO;
using System.Text;
using System.Text.Json;
using TweetViewer.Models;

namespace TweetViewer.Data;

/// <summary>
/// 表示ページ分のツイートについて、tweets.jsonl の該当行を raw_offset/raw_length で
/// シーク読みして X 添付動画 (video エンティティ) を抽出する。
/// 動画は tweet_media に載せない (idx は Python と共有の契約) ため、
/// JsonlImporter.QueryLatestProfile と同じ生行再読み方式をとる。
/// </summary>
public sealed class RawVideoEntityReader(ViewerDatabase db)
{
    /// <summary>tweet_id → 動画リスト。動画の無いツイートはキー自体を含まない。</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<VideoEntity>> ReadForPage(
        IReadOnlyList<TweetRow> rows)
    {
        var result = new Dictionary<string, IReadOnlyList<VideoEntity>>();
        foreach (var group in rows.GroupBy(r => r.Username))
        {
            try
            {
                ReadFromFile(db.JsonlPath(group.Key), group, result);
            }
            catch (Exception)
            {
                // JSONL 消失・オフセット不整合などは該当分のサムネイルが出ないだけにする
            }
        }
        return result;
    }

    private static void ReadFromFile(
        string jsonlPath, IEnumerable<TweetRow> rows,
        Dictionary<string, IReadOnlyList<VideoEntity>> result)
    {
        using var stream = new FileStream(
            jsonlPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        foreach (var row in rows.OrderBy(r => r.RawOffset))
        {
            try
            {
                stream.Seek(row.RawOffset, SeekOrigin.Begin);
                var buf = new byte[row.RawLength];
                stream.ReadExactly(buf);
                var line = Encoding.UTF8.GetString(buf).TrimEnd('\r');
                // JSON パースは動画持ちの行 (ページあたり数件) だけに絞る
                if (!line.Contains("video.twimg.com", StringComparison.Ordinal))
                    continue;
                using var doc = JsonDocument.Parse(line);
                var videos = TweetJsonParser.ExtractVideoEntities(doc.RootElement);
                if (videos.Count > 0)
                    result[row.TweetId] = videos;
            }
            catch (Exception)
            {
                // 行単位で握りつぶす (壊れた行があっても他のツイートは表示する)
            }
        }
    }
}
