namespace TweetViewer.Models;

/// <summary>tags テーブル1行(付与人数はクエリで付加)。</summary>
public sealed record TagRow
{
    public required long TagId { get; init; }
    public required string Name { get; init; }
    public long UserCount { get; init; }
}
