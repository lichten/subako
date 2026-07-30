using Microsoft.Data.Sqlite;
using TweetViewer.Models;

namespace TweetViewer.Data;

/// <summary>
/// タイムライン1ページ分。ArchivesByTweetId は複数アーカイブに存在する tweet_id のみを持つ
/// (既読化時に該当する全アーカイブの未読数を減らすため。表示中でないアーカイブも含む)。
/// </summary>
public sealed record TweetPage(
    IReadOnlyList<TweetRow> Rows,
    IReadOnlyDictionary<string, List<TweetMediaRow>> Media,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ArchivesByTweetId);

public sealed record MediaPageRow(
    string TweetId, int Idx, string Ext, long SortKey, long IdInt,
    string FullText, string CreatedAtUtc, string Username);

public sealed class TweetRepository
{
    private readonly ViewerDatabase _db;

    public TweetRepository(ViewerDatabase db) => _db = db;

    /// <summary>
    /// keyset pagination。after が null なら先頭ページ。
    /// 複数アーカイブを混ぜる場合、同一 tweet_id はアーカイブと検索バケットの両方に
    /// 存在しうる (PK は (username, tweet_id)) ため、窓関数で LIMIT より前に重複排除する。
    /// 代表は実ユーザーアーカイブ優先 (searches/ は劣後)。
    /// LIMIT を dedup の後に置くことで「Rows.Count == limit ⇔ まだ残りがある」の
    /// 既存の意味論 (HasMore 判定) が保たれる。
    /// sort_key / id_int は created_at と tweet id から決定的に導出されるため
    /// 重複コピー間で必ず一致し、カーソル条件・unreadOnly はパーティション単位で
    /// 丸ごと効く (代表の選択やページ境界を歪めない)。
    /// </summary>
    public Task<TweetPage> GetPageAsync(
        IReadOnlyList<string> usernames, bool unreadOnly, (long SortKey, long IdInt)? after, int limit,
        DateRangeFilter? range = null, bool ascending = false, CancellationToken ct = default)
    {
        if (usernames.Count == 0)
        {
            return Task.FromResult(new TweetPage(
                [], new Dictionary<string, List<TweetMediaRow>>(),
                new Dictionary<string, IReadOnlyList<string>>()));
        }
        return Task.Run(() =>
        {
            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();
            var inList = BindInList(cmd, "$u", usernames);
            const string columns = """
                t.tweet_id, t.id_int, t.username, t.created_at_utc, t.sort_key,
                t.tweet_type, t.full_text, t.lang, t.in_reply_to_username,
                t.rt_username, t.rt_display_name, t.rt_text,
                t.quoted_username, t.quoted_display_name, t.quoted_text,
                t.like_count, t.retweet_count, t.reply_count, t.view_count,
                t.media_count, t.raw_offset, t.raw_length,
                t.rt_icon_url, t.quoted_icon_url,
                t.author_username, t.author_display_name, t.author_icon_url,
                (r.tweet_id IS NOT NULL) AS is_read
                """;
            // 並び順はパラメータではなく SQL 文字列で分岐する ($asc を OR で混ぜると
            // 索引レンジに落ちず全走査に退化しうる)。2 列とも同方向にすること
            // (混在ソートは ix_tweets_user_sort の逆走査が効かない)
            var cmp = ascending ? ">" : "<";
            var dir = ascending ? "ASC" : "DESC";
            var filters = $"""
                  AND ($unreadOnly = 0 OR r.tweet_id IS NULL)
                  AND ($noRange = 1 OR (t.sort_key >= $from AND t.sort_key < $to))
                  AND ($noCursor = 1 OR t.sort_key {cmp} $sk
                       OR (t.sort_key = $sk AND t.id_int {cmp} $idi))
                """;
            // 単一アーカイブは PK (username, tweet_id) により重複しないため、
            // 窓関数を通さず ix_tweets_user_sort の索引ストリームで返す
            // (数万ツイート規模のアーカイブで 250ms → 数ms の差が毎ページ効く)
            cmd.CommandText = usernames.Count == 1
                ? $"""
                  SELECT {columns}
                  FROM tweets t
                  LEFT JOIN read_state r ON r.tweet_id = t.tweet_id
                  WHERE t.username = $u0
                  {filters}
                  ORDER BY t.sort_key {dir}, t.id_int {dir}
                  LIMIT $limit
                  """
                : $"""
                  SELECT * FROM (
                      SELECT {columns},
                             ROW_NUMBER() OVER (
                                 PARTITION BY t.tweet_id
                                 ORDER BY (t.username LIKE 'searches/%'), t.username) AS rn
                      FROM tweets t
                      LEFT JOIN read_state r ON r.tweet_id = t.tweet_id
                      WHERE t.username IN ({inList})
                      {filters}
                  )
                  WHERE rn = 1
                  ORDER BY sort_key {dir}, id_int {dir}
                  LIMIT $limit
                  """;
            cmd.Parameters.AddWithValue("$unreadOnly", unreadOnly ? 1 : 0);
            cmd.Parameters.AddWithValue("$noRange", range is null ? 1 : 0);
            cmd.Parameters.AddWithValue("$from", range?.FromEpoch ?? 0);
            cmd.Parameters.AddWithValue("$to", range?.ToEpochExclusive ?? 0);
            cmd.Parameters.AddWithValue("$noCursor", after is null ? 1 : 0);
            cmd.Parameters.AddWithValue("$sk", after?.SortKey ?? 0);
            cmd.Parameters.AddWithValue("$idi", after?.IdInt ?? 0);
            cmd.Parameters.AddWithValue("$limit", limit);

            var rows = new List<TweetRow>();
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    ct.ThrowIfCancellationRequested();
                    rows.Add(new TweetRow
                    {
                        TweetId = reader.GetString(0),
                        IdInt = reader.GetInt64(1),
                        Username = reader.GetString(2),
                        CreatedAtUtc = reader.GetString(3),
                        SortKey = reader.GetInt64(4),
                        Type = (TweetType)reader.GetInt32(5),
                        FullText = reader.GetString(6),
                        Lang = reader.IsDBNull(7) ? null : reader.GetString(7),
                        InReplyToUsername = reader.IsDBNull(8) ? null : reader.GetString(8),
                        RtUsername = reader.IsDBNull(9) ? null : reader.GetString(9),
                        RtDisplayName = reader.IsDBNull(10) ? null : reader.GetString(10),
                        RtText = reader.IsDBNull(11) ? null : reader.GetString(11),
                        QuotedUsername = reader.IsDBNull(12) ? null : reader.GetString(12),
                        QuotedDisplayName = reader.IsDBNull(13) ? null : reader.GetString(13),
                        QuotedText = reader.IsDBNull(14) ? null : reader.GetString(14),
                        LikeCount = reader.GetInt64(15),
                        RetweetCount = reader.GetInt64(16),
                        ReplyCount = reader.GetInt64(17),
                        ViewCount = reader.GetInt64(18),
                        MediaCount = reader.GetInt32(19),
                        RawOffset = reader.GetInt64(20),
                        RawLength = reader.GetInt64(21),
                        RtIconUrl = reader.IsDBNull(22) ? null : reader.GetString(22),
                        QuotedIconUrl = reader.IsDBNull(23) ? null : reader.GetString(23),
                        AuthorUsername = reader.IsDBNull(24) ? null : reader.GetString(24),
                        AuthorDisplayName = reader.IsDBNull(25) ? null : reader.GetString(25),
                        AuthorIconUrl = reader.IsDBNull(26) ? null : reader.GetString(26),
                        IsRead = reader.GetInt64(27) != 0,
                    });
                }
            }

            var media = LoadMedia(conn, rows.Where(r => r.MediaCount > 0).Select(r => r.TweetId).ToList());
            var archives = LoadDuplicateArchives(conn, rows.Select(r => r.TweetId).ToList());
            return new TweetPage(rows, media, archives);
        }, ct);
    }

    /// <summary>
    /// ページ内 tweet_id ごとに、それを含む全アーカイブの username を引く。
    /// read_state は tweet_id 単位で全アーカイブ共通のため、既読化は表示中でない
    /// アーカイブ (タグフィルタで隠れている等) の未読数も実際に減らす。
    /// そのため表示中集合では絞らない。辞書には 2 アーカイブ以上に存在するものだけ載せる。
    /// </summary>
    private static Dictionary<string, IReadOnlyList<string>> LoadDuplicateArchives(
        SqliteConnection conn, IReadOnlyList<string> tweetIds)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>();
        if (tweetIds.Count == 0)
            return result;

        using var cmd = conn.CreateCommand();
        var inList = BindInList(cmd, "$id", tweetIds);
        cmd.CommandText =
            $"SELECT tweet_id, username FROM tweets WHERE tweet_id IN ({inList}) ORDER BY tweet_id";
        var all = new Dictionary<string, List<string>>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var tweetId = reader.GetString(0);
            if (!all.TryGetValue(tweetId, out var list))
                all[tweetId] = list = new List<string>();
            list.Add(reader.GetString(1));
        }
        foreach (var (tweetId, list) in all)
        {
            if (list.Count > 1)
                result[tweetId] = list;
        }
        return result;
    }

    /// <summary>IN 句のパラメータを動的にバインドし、"$p0,$p1,..." を返す。</summary>
    private static string BindInList(SqliteCommand cmd, string prefix, IReadOnlyList<string> values)
    {
        var names = new List<string>(values.Count);
        for (var i = 0; i < values.Count; i++)
        {
            var name = $"{prefix}{i}";
            names.Add(name);
            cmd.Parameters.AddWithValue(name, values[i]);
        }
        return string.Join(",", names);
    }

    private static Dictionary<string, List<TweetMediaRow>> LoadMedia(
        SqliteConnection conn, IReadOnlyList<string> tweetIds)
    {
        var result = new Dictionary<string, List<TweetMediaRow>>();
        if (tweetIds.Count == 0)
            return result;

        using var cmd = conn.CreateCommand();
        var names = new List<string>();
        for (var i = 0; i < tweetIds.Count; i++)
        {
            var name = $"$id{i}";
            names.Add(name);
            cmd.Parameters.AddWithValue(name, tweetIds[i]);
        }
        cmd.CommandText =
            $"SELECT tweet_id, idx, source_url, ext, origin FROM tweet_media WHERE tweet_id IN ({string.Join(",", names)}) ORDER BY tweet_id, idx";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var row = new TweetMediaRow(
                reader.GetString(0), reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetString(3),
                (MediaOrigin)reader.GetInt32(4));
            if (!result.TryGetValue(row.TweetId, out var list))
                result[row.TweetId] = list = new List<TweetMediaRow>();
            list.Add(row);
        }
        return result;
    }

    /// <summary>
    /// メディア欄用: 本人の投稿画像のみ (origin=0、RT 除外) を ascending=false なら新しい順に。
    /// keyset カーソルは (sort_key, id_int, idx) の3要素。
    /// 重複排除は GetPageAsync と同じ理由・同じ規則で、パーティションは (tweet_id, idx)
    /// (tweet_id だけだと複数枚画像が 1 枚に潰れる)。
    /// </summary>
    public Task<List<MediaPageRow>> GetMediaPageAsync(
        IReadOnlyList<string> usernames, (long SortKey, long IdInt, int Idx)? after, int limit,
        DateRangeFilter? range = null, bool ascending = false, CancellationToken ct = default)
    {
        if (usernames.Count == 0)
            return Task.FromResult(new List<MediaPageRow>());
        return Task.Run(() =>
        {
            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();
            var inList = BindInList(cmd, "$u", usernames);
            // 方向の分岐は GetPageAsync と同じ理由で SQL 文字列側で行う。
            // 第 3 要素 idx はツイート内の並びで常に昇順のため、比較 (> $ix) も
            // ORDER BY (idx ASC) も方向によらず不変な点に注意
            var cmp = ascending ? ">" : "<";
            var dir = ascending ? "ASC" : "DESC";
            var filters = $"""
                  AND m.origin = 0
                  AND t.tweet_type != 1
                  AND ($noRange = 1 OR (t.sort_key >= $from AND t.sort_key < $to))
                  AND ($noCursor = 1
                       OR t.sort_key {cmp} $sk
                       OR (t.sort_key = $sk AND t.id_int {cmp} $ii)
                       OR (t.sort_key = $sk AND t.id_int = $ii AND m.idx > $ix))
                """;
            // 単一アーカイブは重複しないため窓関数を通らない (GetPageAsync と同じ理由)
            cmd.CommandText = usernames.Count == 1
                ? $"""
                  SELECT m.tweet_id, m.idx, m.ext, t.sort_key, t.id_int,
                         t.full_text, t.created_at_utc, t.username
                  FROM tweet_media m
                  JOIN tweets t ON t.tweet_id = m.tweet_id
                  WHERE t.username = $u0
                  {filters}
                  ORDER BY t.sort_key {dir}, t.id_int {dir}, m.idx ASC
                  LIMIT $limit
                  """
                : $"""
                  SELECT * FROM (
                      SELECT m.tweet_id, m.idx, m.ext, t.sort_key, t.id_int,
                             t.full_text, t.created_at_utc, t.username,
                             ROW_NUMBER() OVER (
                                 PARTITION BY m.tweet_id, m.idx
                                 ORDER BY (t.username LIKE 'searches/%'), t.username) AS rn
                      FROM tweet_media m
                      JOIN tweets t ON t.tweet_id = m.tweet_id
                      WHERE t.username IN ({inList})
                      {filters}
                  )
                  WHERE rn = 1
                  ORDER BY sort_key {dir}, id_int {dir}, idx ASC
                  LIMIT $limit
                  """;
            cmd.Parameters.AddWithValue("$noRange", range is null ? 1 : 0);
            cmd.Parameters.AddWithValue("$from", range?.FromEpoch ?? 0);
            cmd.Parameters.AddWithValue("$to", range?.ToEpochExclusive ?? 0);
            cmd.Parameters.AddWithValue("$noCursor", after is null ? 1 : 0);
            cmd.Parameters.AddWithValue("$sk", after?.SortKey ?? 0);
            cmd.Parameters.AddWithValue("$ii", after?.IdInt ?? 0);
            cmd.Parameters.AddWithValue("$ix", after?.Idx ?? 0);
            cmd.Parameters.AddWithValue("$limit", limit);

            var rows = new List<MediaPageRow>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                ct.ThrowIfCancellationRequested();
                rows.Add(new MediaPageRow(
                    TweetId: reader.GetString(0),
                    Idx: reader.GetInt32(1),
                    Ext: reader.GetString(2),
                    SortKey: reader.GetInt64(3),
                    IdInt: reader.GetInt64(4),
                    FullText: reader.GetString(5),
                    CreatedAtUtc: reader.GetString(6),
                    Username: reader.GetString(7)));
            }
            return rows;
        }, ct);
    }

    /// <summary>表示対象全体の sort_key の最小/最大 (期間フィルタの年リスト用)。行が無ければ null。</summary>
    public Task<(long Min, long Max)?> GetDateBoundsAsync(
        IReadOnlyList<string> usernames, CancellationToken ct = default)
    {
        if (usernames.Count == 0)
            return Task.FromResult<(long, long)?>(null);
        return Task.Run<(long, long)?>(() =>
        {
            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();
            var inList = BindInList(cmd, "$u", usernames);
            cmd.CommandText =
                $"SELECT MIN(sort_key), MAX(sort_key) FROM tweets WHERE username IN ({inList})";
            using var reader = cmd.ExecuteReader();
            if (!reader.Read() || reader.IsDBNull(0))
                return null;
            return (reader.GetInt64(0), reader.GetInt64(1));
        }, ct);
    }

    /// <summary>手動トグル用の即時書込。</summary>
    public async Task SetReadAsync(string tweetId, string username, bool read)
    {
        await _db.WriteLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                using var conn = _db.OpenConnection();
                using var cmd = conn.CreateCommand();
                if (read)
                {
                    cmd.CommandText = """
                        INSERT OR IGNORE INTO read_state (tweet_id, username, read_at)
                        VALUES ($id, $u, $t)
                        """;
                    cmd.Parameters.AddWithValue("$id", tweetId);
                    cmd.Parameters.AddWithValue("$u", username);
                    cmd.Parameters.AddWithValue("$t", JsonlImporter.UtcNow());
                }
                else
                {
                    cmd.CommandText = "DELETE FROM read_state WHERE tweet_id = $id";
                    cmd.Parameters.AddWithValue("$id", tweetId);
                }
                cmd.ExecuteNonQuery();
            }).ConfigureAwait(false);
        }
        finally
        {
            _db.WriteLock.Release();
        }
    }
}
