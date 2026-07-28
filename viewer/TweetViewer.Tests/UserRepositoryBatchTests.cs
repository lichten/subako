using System.IO;
using Microsoft.Data.Sqlite;
using TweetViewer.Data;

namespace TweetViewer.Tests;

/// <summary>フォロー一括登録が使う UserRepository.AddManyAsync の検証。</summary>
public sealed class UserRepositoryBatchTests : IDisposable
{
    private readonly string _dataDir;
    private readonly ViewerDatabase _db;
    private readonly UserRepository _repo;

    public UserRepositoryBatchTests()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), "SubakoTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDir);
        _db = new ViewerDatabase(_dataDir);
        _db.EnsureCreated();
        _repo = new UserRepository(_db);
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
    public async Task 新規登録できたものだけ返す()
    {
        var first = await _repo.AddManyAsync([("alice", "Alice"), ("bob", "Bob")]);
        var second = await _repo.AddManyAsync([("alice", "Alice"), ("carol", "Carol")]);

        Assert.Equal(new[] { "alice", "bob" }, first);
        Assert.Equal(new[] { "carol" }, second);
        Assert.Equal(3, (await _repo.GetAllAsync()).Count);
    }

    [Fact]
    public async Task display_nameは新規行にだけ入り既存行を上書きしない()
    {
        await _repo.AddManyAsync([("alice", "最初の名前")]);
        await _repo.AddManyAsync([("alice", "あとから来た名前")]);

        var alice = (await _repo.GetAllAsync()).Single(u => u.Username == "alice");
        Assert.Equal("最初の名前", alice.DisplayName);
    }

    [Fact]
    public async Task display_nameがnullでも登録できる()
    {
        await _repo.AddManyAsync([("alice", null)]);

        var alice = (await _repo.GetAllAsync()).Single(u => u.Username == "alice");
        Assert.Null(alice.DisplayName);
    }

    [Fact]
    public async Task 空入力は何もしない()
    {
        Assert.Empty(await _repo.AddManyAsync([]));
        Assert.Empty(await _repo.GetAllAsync());
    }

    [Fact]
    public async Task データフォルダは作らない()
    {
        // 数千件の空フォルダは毎起動の RegisterExistingDataDirsAsync を重くするだけで、
        // 実際の取得時に Python 側が mkdir する (docs/data-layer.md §1.7)。
        // AddAsync (単体) との意図的な差分なので固定しておく
        await _repo.AddManyAsync([("alice", "Alice")]);

        Assert.False(Directory.Exists(_db.UserDir("alice")));
    }
}
