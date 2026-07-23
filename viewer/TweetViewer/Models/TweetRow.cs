namespace TweetViewer.Models;

public enum TweetType
{
    Tweet = 0,
    Retweet = 1,
    Reply = 2,
    Quote = 3,
}

/// <summary>tweets テーブル1行 + LEFT JOIN read_state の結果。</summary>
public sealed record TweetRow
{
    public required string TweetId { get; init; }
    public required long IdInt { get; init; }
    public required string Username { get; init; }
    public required string CreatedAtUtc { get; init; }
    public required long SortKey { get; init; }
    public required TweetType Type { get; init; }
    public required string FullText { get; init; }
    public string? Lang { get; init; }
    public string? InReplyToUsername { get; init; }
    public string? RtUsername { get; init; }
    public string? RtDisplayName { get; init; }
    public string? RtText { get; init; }
    public string? QuotedUsername { get; init; }
    public string? QuotedDisplayName { get; init; }
    public string? QuotedText { get; init; }
    public long LikeCount { get; init; }
    public long RetweetCount { get; init; }
    public long ReplyCount { get; init; }
    public long ViewCount { get; init; }
    public int MediaCount { get; init; }
    public long RawOffset { get; init; }
    public long RawLength { get; init; }
    public bool IsRead { get; init; }
}

public sealed record TweetMediaRow(string TweetId, int Index, string? SourceUrl, string Ext);
