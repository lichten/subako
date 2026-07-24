using System.IO;
using Microsoft.Data.Sqlite;
using TweetViewer.Data;

namespace TweetViewer.Tests;

/// <summary>tags / user_tags の CRUD 検証。</summary>
public sealed class TagRepositoryTests : IDisposable
{
    private readonly string _dataDir;
    private readonly ViewerDatabase _db;
    private readonly TagRepository _repo;

    public TagRepositoryTests()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), "TweetViewerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDir);
        _db = new ViewerDatabase(_dataDir);
        _db.EnsureCreated();
        _repo = new TagRepository(_db);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dataDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task AddAsync_同名は大文字小文字無視で同一IDを返す()
    {
        var id1 = await _repo.AddAsync("絵師");
        var id2 = await _repo.AddAsync("絵師");
        var id3 = await _repo.AddAsync("Artist");
        var id4 = await _repo.AddAsync("artist");

        Assert.Equal(id1, id2);
        Assert.Equal(id3, id4);
        Assert.NotEqual(id1, id3);
    }

    [Fact]
    public async Task Assign_Unassign_GetAssignmentsで往復確認()
    {
        var tagA = await _repo.AddAsync("A");
        var tagB = await _repo.AddAsync("B");

        await _repo.AssignAsync("alice", tagA);
        await _repo.AssignAsync("alice", tagB);
        await _repo.AssignAsync("alice", tagA); // 重複 Assign は例外にならない
        await _repo.AssignAsync("bob", tagA);

        var map = await _repo.GetAssignmentsAsync();
        Assert.Equal(new[] { tagA, tagB }, map["alice"].Order());
        Assert.Same(map["alice"], map["ALICE"]); // 大文字小文字無視で引ける
        Assert.Equal(new[] { tagA }, map["bob"]);

        await _repo.UnassignAsync("alice", tagA);
        map = await _repo.GetAssignmentsAsync();
        Assert.Equal(new[] { tagB }, map["alice"]);
        Assert.Equal(new[] { tagA }, map["bob"]);
    }

    [Fact]
    public async Task 検索バケットIDにも付与できる()
    {
        var tag = await _repo.AddAsync("A");
        await _repo.AssignAsync("searches/kw-12345678", tag);

        var map = await _repo.GetAssignmentsAsync();
        Assert.Equal(new[] { tag }, map["searches/kw-12345678"]);
        var all = await _repo.GetAllAsync();
        Assert.Equal(1, all.Single().UserCount);
    }

    [Fact]
    public async Task DeleteAsync_user_tagsも同時に消える()
    {
        var tag = await _repo.AddAsync("A");
        await _repo.AssignAsync("alice", tag);
        await _repo.AssignAsync("bob", tag);

        await _repo.DeleteAsync(tag);

        Assert.Empty(await _repo.GetAllAsync());
        Assert.Empty(await _repo.GetAssignmentsAsync());
    }

    [Fact]
    public async Task GetAllAsync_UserCountが付与人数を返しname昇順()
    {
        var tagB = await _repo.AddAsync("b-tag");
        var tagA = await _repo.AddAsync("A-tag");
        await _repo.AssignAsync("alice", tagB);
        await _repo.AssignAsync("bob", tagB);

        var all = await _repo.GetAllAsync();

        Assert.Equal(2, all.Count);
        // name 昇順 (大文字小文字無視)
        Assert.Equal(tagA, all[0].TagId);
        Assert.Equal(tagB, all[1].TagId);
        Assert.Equal(0, all[0].UserCount);
        Assert.Equal(2, all[1].UserCount);
    }
}
