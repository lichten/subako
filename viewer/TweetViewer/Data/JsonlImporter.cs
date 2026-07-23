using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;
using TweetViewer.Models;

namespace TweetViewer.Data;

public sealed record ImportResult(int NewTweets, int SkippedLines, long NewOffset);

public sealed record ImportProgress(long BytesDone, long BytesTotal, int Imported);

/// <summary>
/// tweets.jsonl → SQLite の差分取込。users.jsonl_offset のバイトオフセットから
/// 再開し、完結行(\n 終端)のみ取り込む。fetcher の並行追記に対して安全。
/// </summary>
public sealed class JsonlImporter
{
    private const int BatchSize = 500;

    private readonly ViewerDatabase _db;

    public JsonlImporter(ViewerDatabase db) => _db = db;

    public async Task<ImportResult> ImportUserAsync(
        string username, IProgress<ImportProgress>? progress = null, CancellationToken ct = default)
    {
        var jsonlPath = _db.JsonlPath(username);
        if (!File.Exists(jsonlPath))
            return new ImportResult(0, 0, 0);

        await _db.WriteLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => ImportCore(username, jsonlPath, progress, ct), ct)
                .ConfigureAwait(false);
        }
        finally
        {
            _db.WriteLock.Release();
        }
    }

    /// <summary>該当ユーザーの派生データを削除して JSONL から再取込。read_state は不触。</summary>
    public async Task<ImportResult> RebuildUserAsync(
        string username, IProgress<ImportProgress>? progress = null, CancellationToken ct = default)
    {
        await _db.WriteLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await Task.Run(() => ResetDerived(username), ct).ConfigureAwait(false);
        }
        finally
        {
            _db.WriteLock.Release();
        }
        return await ImportUserAsync(username, progress, ct).ConfigureAwait(false);
    }

    private void ResetDerived(string username)
    {
        using var conn = _db.OpenConnection();
        using var tx = conn.BeginTransaction();
        Execute(conn, tx,
            "DELETE FROM tweet_media WHERE tweet_id IN (SELECT tweet_id FROM tweets WHERE username = $u)",
            ("$u", username));
        Execute(conn, tx, "DELETE FROM tweets WHERE username = $u", ("$u", username));
        Execute(conn, tx, "UPDATE users SET jsonl_offset = 0 WHERE username = $u", ("$u", username));
        tx.Commit();
    }

    private ImportResult ImportCore(
        string username, string jsonlPath, IProgress<ImportProgress>? progress, CancellationToken ct)
    {
        using var conn = _db.OpenConnection();

        var offset = GetOffset(conn, username);
        using var stream = new FileStream(
            jsonlPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        if (offset > stream.Length)
        {
            // JSONL が作り直された(短くなった)→ 派生データを破棄して最初から
            ResetDerivedOn(conn, username);
            offset = 0;
        }

        stream.Seek(offset, SeekOrigin.Begin);
        var reader = new ByteLineReader(stream);

        var imported = 0;
        var skipped = 0;
        string? latestDisplayName = null;

        while (!ct.IsCancellationRequested)
        {
            var batch = new List<(ParsedTweet Tweet, long EndOffset)>();
            long batchEndOffset = offset;
            while (batch.Count < BatchSize && reader.TryReadLine(out var line, out var lineOffset, out var lineLength, out var endOffset))
            {
                batchEndOffset = endOffset;
                var parsed = TweetJsonParser.Parse(line, username, lineOffset, lineLength);
                if (parsed is null)
                {
                    skipped++;
                    continue;
                }
                batch.Add((parsed, endOffset));
            }
            if (batchEndOffset == offset && batch.Count == 0)
                break;

            using (var tx = conn.BeginTransaction())
            {
                foreach (var (tweet, _) in batch)
                {
                    if (InsertTweet(conn, tx, tweet))
                        imported++;
                }
                Execute(conn, tx, "UPDATE users SET jsonl_offset = $o WHERE username = $u",
                    ("$o", batchEndOffset), ("$u", username));
                tx.Commit();
            }
            offset = batchEndOffset;
            progress?.Report(new ImportProgress(offset, stream.Length, imported));
        }

        ct.ThrowIfCancellationRequested();

        // display_name は最新ツイートの user.display_name で更新(取込があった場合のみ)
        if (imported > 0)
            latestDisplayName = QueryLatestDisplayName(conn, username);

        using (var tx = conn.BeginTransaction())
        {
            Execute(conn, tx,
                "UPDATE users SET last_import_at = $t WHERE username = $u",
                ("$t", UtcNow()), ("$u", username));
            if (latestDisplayName is not null)
                Execute(conn, tx,
                    "UPDATE users SET display_name = $d WHERE username = $u",
                    ("$d", latestDisplayName), ("$u", username));
            tx.Commit();
        }

        return new ImportResult(imported, skipped, offset);
    }

    private string? QueryLatestDisplayName(SqliteConnection conn, string username)
    {
        // 生 JSONL から最新ツイートの表示名をシーク読みする
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT raw_offset, raw_length FROM tweets
            WHERE username = $u ORDER BY sort_key DESC, id_int DESC LIMIT 1
            """;
        cmd.Parameters.AddWithValue("$u", username);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return null;
        var rawOffset = reader.GetInt64(0);
        var rawLength = reader.GetInt64(1);
        try
        {
            using var stream = new FileStream(
                _db.JsonlPath(username), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            stream.Seek(rawOffset, SeekOrigin.Begin);
            var buf = new byte[rawLength];
            stream.ReadExactly(buf);
            var line = Encoding.UTF8.GetString(buf);
            using var doc = System.Text.Json.JsonDocument.Parse(line);
            return doc.RootElement.TryGetProperty("user", out var user) &&
                   user.ValueKind == System.Text.Json.JsonValueKind.Object &&
                   user.TryGetProperty("display_name", out var dn) &&
                   dn.ValueKind == System.Text.Json.JsonValueKind.String
                ? dn.GetString()
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void ResetDerivedOn(SqliteConnection conn, string username)
    {
        using var tx = conn.BeginTransaction();
        Execute(conn, tx,
            "DELETE FROM tweet_media WHERE tweet_id IN (SELECT tweet_id FROM tweets WHERE username = $u)",
            ("$u", username));
        Execute(conn, tx, "DELETE FROM tweets WHERE username = $u", ("$u", username));
        Execute(conn, tx, "UPDATE users SET jsonl_offset = 0 WHERE username = $u", ("$u", username));
        tx.Commit();
    }

    private static long GetOffset(SqliteConnection conn, string username)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT jsonl_offset FROM users WHERE username = $u";
        cmd.Parameters.AddWithValue("$u", username);
        return cmd.ExecuteScalar() is long o ? o : 0;
    }

    private static bool InsertTweet(SqliteConnection conn, SqliteTransaction tx, ParsedTweet parsed)
    {
        var r = parsed.Row;
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT OR IGNORE INTO tweets (
              tweet_id, id_int, username, created_at_utc, sort_key, tweet_type,
              full_text, lang, in_reply_to_username,
              rt_username, rt_display_name, rt_text,
              quoted_username, quoted_display_name, quoted_text,
              like_count, retweet_count, reply_count, view_count,
              media_count, raw_offset, raw_length
            ) VALUES (
              $id, $idi, $u, $cat, $sk, $ty,
              $tx, $lg, $irtu,
              $rtu, $rtd, $rtt,
              $qu, $qd, $qt,
              $lc, $rc, $pc, $vc,
              $mc, $ro, $rl
            )
            """;
        var p = cmd.Parameters;
        p.AddWithValue("$id", r.TweetId);
        p.AddWithValue("$idi", r.IdInt);
        p.AddWithValue("$u", r.Username);
        p.AddWithValue("$cat", r.CreatedAtUtc);
        p.AddWithValue("$sk", r.SortKey);
        p.AddWithValue("$ty", (int)r.Type);
        p.AddWithValue("$tx", r.FullText);
        p.AddWithValue("$lg", (object?)r.Lang ?? DBNull.Value);
        p.AddWithValue("$irtu", (object?)r.InReplyToUsername ?? DBNull.Value);
        p.AddWithValue("$rtu", (object?)r.RtUsername ?? DBNull.Value);
        p.AddWithValue("$rtd", (object?)r.RtDisplayName ?? DBNull.Value);
        p.AddWithValue("$rtt", (object?)r.RtText ?? DBNull.Value);
        p.AddWithValue("$qu", (object?)r.QuotedUsername ?? DBNull.Value);
        p.AddWithValue("$qd", (object?)r.QuotedDisplayName ?? DBNull.Value);
        p.AddWithValue("$qt", (object?)r.QuotedText ?? DBNull.Value);
        p.AddWithValue("$lc", r.LikeCount);
        p.AddWithValue("$rc", r.RetweetCount);
        p.AddWithValue("$pc", r.ReplyCount);
        p.AddWithValue("$vc", r.ViewCount);
        p.AddWithValue("$mc", r.MediaCount);
        p.AddWithValue("$ro", r.RawOffset);
        p.AddWithValue("$rl", r.RawLength);
        var inserted = cmd.ExecuteNonQuery() > 0;

        if (inserted)
        {
            foreach (var m in parsed.Media)
            {
                using var mc = conn.CreateCommand();
                mc.Transaction = tx;
                mc.CommandText = """
                    INSERT OR IGNORE INTO tweet_media (tweet_id, idx, source_url, ext)
                    VALUES ($id, $ix, $url, $ext)
                    """;
                mc.Parameters.AddWithValue("$id", m.TweetId);
                mc.Parameters.AddWithValue("$ix", m.Index);
                mc.Parameters.AddWithValue("$url", (object?)m.SourceUrl ?? DBNull.Value);
                mc.Parameters.AddWithValue("$ext", m.Ext);
                mc.ExecuteNonQuery();
            }
        }
        return inserted;
    }

    private static void Execute(
        SqliteConnection conn, SqliteTransaction tx, string sql, params (string, object)[] args)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value);
        cmd.ExecuteNonQuery();
    }

    internal static string UtcNow() =>
        DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// バイト位置を追跡しながら UTF-8 の \n 終端行を読む。末尾の不完全行(\n なし)は
/// 読まずに終了する — fetcher が並行追記中でも完結行だけを取り込むため。
/// </summary>
public sealed class ByteLineReader
{
    private readonly Stream _stream;
    private readonly byte[] _buffer = new byte[64 * 1024];
    private int _bufLen;
    private int _bufPos;
    private long _position;
    private readonly MemoryStream _pending = new();

    public ByteLineReader(Stream stream)
    {
        _stream = stream;
        _position = stream.Position;
    }

    /// <summary>
    /// 1行読む。line は改行を除いた文字列、lineOffset/lineLength は改行を含まない
    /// バイト範囲、endOffset は改行の次のバイト位置(=次回再開オフセット)。
    /// </summary>
    public bool TryReadLine(out string line, out long lineOffset, out long lineLength, out long endOffset)
    {
        var lineStart = _position - _pending.Length;
        while (true)
        {
            if (_bufPos >= _bufLen)
            {
                _bufLen = _stream.Read(_buffer, 0, _buffer.Length);
                _bufPos = 0;
                if (_bufLen == 0)
                {
                    // 末尾の不完全行: 取り込まない(_pending は保持し次回に備える)
                    line = "";
                    lineOffset = lineLength = endOffset = 0;
                    return false;
                }
            }

            var nl = Array.IndexOf(_buffer, (byte)'\n', _bufPos, _bufLen - _bufPos);
            if (nl < 0)
            {
                _pending.Write(_buffer, _bufPos, _bufLen - _bufPos);
                _position += _bufLen - _bufPos;
                _bufPos = _bufLen;
                continue;
            }

            _pending.Write(_buffer, _bufPos, nl - _bufPos);
            _position += nl - _bufPos + 1;   // +1 = \n
            _bufPos = nl + 1;

            var bytes = _pending.ToArray();
            _pending.SetLength(0);

            lineOffset = lineStart;
            lineLength = bytes.Length;
            endOffset = _position;
            // CR を除去(CRLF 保険)
            var text = Encoding.UTF8.GetString(bytes);
            line = text.TrimEnd('\r');
            return true;
        }
    }
}
