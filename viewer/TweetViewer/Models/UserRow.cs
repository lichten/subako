namespace TweetViewer.Models;

/// <summary>users テーブル1行(未読数はクエリで付加)。</summary>
public sealed record UserRow
{
    public required string Username { get; init; }
    public string? DisplayName { get; init; }
    public string? IconUrl { get; init; }
    public required string AddedAt { get; init; }
    public string? LastImportAt { get; init; }
    public long JsonlOffset { get; init; }
    public long TweetCount { get; init; }
    public long UnreadCount { get; init; }
}
