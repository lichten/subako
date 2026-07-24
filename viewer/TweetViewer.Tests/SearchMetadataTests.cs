using System.IO;
using TweetViewer.Data;

namespace TweetViewer.Tests;

/// <summary>search.json の read-modify-write 検証。</summary>
public sealed class SearchMetadataTests : IDisposable
{
    private readonly string _dir;

    public SearchMetadataTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "TweetViewerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void WriteAndReadRoundTrip()
    {
        SearchMetadata.Write(_dir, "スレスパ2 lang:ja", "スレスパ2 関連");
        var read = SearchMetadata.TryRead(_dir);
        Assert.NotNull(read);
        Assert.Equal("スレスパ2 lang:ja", read.Value.Query);
        Assert.Equal("スレスパ2 関連", read.Value.Name);
        Assert.NotNull(read.Value.CreatedAt);
    }

    [Fact]
    public void UpdatePreservesCreatedAtAndRemovesName()
    {
        File.WriteAllText(Path.Combine(_dir, "search.json"),
            """{"query": "old", "name": "旧名称", "created_at": "2026-01-01T00:00:00Z"}""");

        SearchMetadata.Write(_dir, "new query", name: null);

        var read = SearchMetadata.TryRead(_dir);
        Assert.Equal("new query", read!.Value.Query);
        Assert.Null(read.Value.Name);                          // null 指定でキー削除
        Assert.Equal("2026-01-01T00:00:00Z", read.Value.CreatedAt); // 他キーは保持
    }

    [Fact]
    public void WriteOverBrokenFileRecreates()
    {
        File.WriteAllText(Path.Combine(_dir, "search.json"), "{broken");
        SearchMetadata.Write(_dir, "q", "n");
        var read = SearchMetadata.TryRead(_dir);
        Assert.Equal("q", read!.Value.Query);
        Assert.Equal("n", read.Value.Name);
    }

    [Fact]
    public void TryReadMissingOrBrokenReturnsNull()
    {
        Assert.Null(SearchMetadata.TryRead(_dir));
        File.WriteAllText(Path.Combine(_dir, "search.json"), "{broken");
        Assert.Null(SearchMetadata.TryRead(_dir));
    }
}
