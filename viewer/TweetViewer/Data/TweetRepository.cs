using Microsoft.Data.Sqlite;
using TweetViewer.Models;

namespace TweetViewer.Data;

public sealed record TweetPage(IReadOnlyList<TweetRow> Rows, IReadOnlyDictionary<string, List<TweetMediaRow>> Media);

public sealed record MediaPageRow(
    string TweetId, int Idx, string Ext, long SortKey, long IdInt,
    string FullText, string CreatedAtUtc);

public sealed class TweetRepository
{
    private readonly ViewerDatabase _db;

    public TweetRepository(ViewerDatabase db) => _db = db;

    /// <summary>keyset pagination。after が null なら先頭ページ。</summary>
    public Task<TweetPage> GetPageAsync(
        string username, bool unreadOnly, (long SortKey, long IdInt)? after, int limit,
        CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT t.tweet_id, t.id_int, t.username, t.created_at_utc, t.sort_key,
                       t.tweet_type, t.full_text, t.lang, t.in_reply_to_username,
                       t.rt_username, t.rt_display_name, t.rt_text,
                       t.quoted_username, t.quoted_display_name, t.quoted_text,
                       t.like_count, t.retweet_count, t.reply_count, t.view_count,
                       t.media_count, t.raw_offset, t.raw_length,
                       t.rt_icon_url, t.quoted_icon_url,
                       (r.tweet_id IS NOT NULL) AS is_read
                FROM tweets t
                LEFT JOIN read_state r ON r.tweet_id = t.tweet_id
                WHERE t.username = $u
                  AND ($unreadOnly = 0 OR r.tweet_id IS NULL)
                  AND ($noCursor = 1 OR t.sort_key < $sk
                       OR (t.sort_key = $sk AND t.id_int < $idi))
                ORDER BY t.sort_key DESC, t.id_int DESC
                LIMIT $limit
                """;
            cmd.Parameters.AddWithValue("$u", username);
            cmd.Parameters.AddWithValue("$unreadOnly", unreadOnly ? 1 : 0);
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
                        IsRead = reader.GetInt64(24) != 0,
                    });
                }
            }

            var media = LoadMedia(conn, rows.Where(r => r.MediaCount > 0).Select(r => r.TweetId).ToList());
            return new TweetPage(rows, media);
        }, ct);
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
    /// メディア欄用: 本人の投稿画像のみ (origin=0、RT 除外) を新しい順に。
    /// keyset カーソルは (sort_key, id_int, idx) の3要素。
    /// </summary>
    public Task<List<MediaPageRow>> GetMediaPageAsync(
        string username, (long SortKey, long IdInt, int Idx)? after, int limit,
        CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT m.tweet_id, m.idx, m.ext, t.sort_key, t.id_int,
                       t.full_text, t.created_at_utc
                FROM tweet_media m
                JOIN tweets t ON t.tweet_id = m.tweet_id
                WHERE t.username = $u
                  AND m.origin = 0
                  AND t.tweet_type != 1
                  AND ($noCursor = 1
                       OR t.sort_key < $sk
                       OR (t.sort_key = $sk AND t.id_int < $ii)
                       OR (t.sort_key = $sk AND t.id_int = $ii AND m.idx > $ix))
                ORDER BY t.sort_key DESC, t.id_int DESC, m.idx ASC
                LIMIT $limit
                """;
            cmd.Parameters.AddWithValue("$u", username);
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
                    CreatedAtUtc: reader.GetString(6)));
            }
            return rows;
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
