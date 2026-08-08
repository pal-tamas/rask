using System.Text;

namespace Rask.SQLite.Browser.Tests;

public sealed class IndexedDbSnapshotStoreTests : IDisposable
{
    private const string StoreName = "rask-sqlite-app";

    private readonly string _temp = Directory.CreateTempSubdirectory("rask-idb-store").FullName;
    private readonly FakeIndexedDb _db = new();

    private IndexedDbSnapshotStore Store() => new(_db, StoreName);

    public void Dispose() => Directory.Delete(_temp, recursive: true);

    private string WriteTempFile(string content)
    {
        var path = Path.Combine(_temp, $"{Guid.NewGuid():N}.db");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(content));
        return path;
    }

    [Fact]
    public async Task Save_StoresTheFileBytesUnderTheSnapshotName()
    {
        var source = WriteTempFile("database");

        await Store().SaveAsync(source, "app-20260808-120000000.db", CancellationToken.None);

        Assert.Equal(
            "database",
            Encoding.UTF8.GetString(_db.Store(StoreName).Values["app-20260808-120000000.db"]));
    }

    // The snapshotter hands over a temp file it expects to be consumed. In the browser that file sits in
    // the runtime's in-memory filesystem, so leaving it behind spends the tab's heap.
    [Fact]
    public async Task Save_ConsumesTheSourceFile()
    {
        var source = WriteTempFile("database");

        await Store().SaveAsync(source, "app-20260808-120000000.db", CancellationToken.None);

        Assert.False(File.Exists(source));
    }

    [Fact]
    public async Task Prune_KeepsTheNewestAndDropsTheRest()
    {
        var store = Store();
        await store.SaveAsync(WriteTempFile("1"), "app-20260808-120000000.db", CancellationToken.None);
        await store.SaveAsync(WriteTempFile("2"), "app-20260808-130000000.db", CancellationToken.None);
        await store.SaveAsync(WriteTempFile("3"), "app-20260808-140000000.db", CancellationToken.None);

        await store.PruneAsync(2, CancellationToken.None);

        Assert.Equal(
            ["app-20260808-130000000.db", "app-20260808-140000000.db"],
            _db.Store(StoreName).Values.Keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    // Retention below one would empty the store, leaving nothing to restore from.
    [Fact]
    public async Task Prune_NeverDropsEverything()
    {
        var store = Store();
        await store.SaveAsync(WriteTempFile("1"), "app-20260808-120000000.db", CancellationToken.None);

        await store.PruneAsync(0, CancellationToken.None);

        Assert.Single(_db.Store(StoreName).Values);
    }

    [Fact]
    public async Task ReadNewest_ReturnsTheLatestByTimestamp()
    {
        var store = Store();
        await store.SaveAsync(WriteTempFile("old"), "app-20260808-120000000.db", CancellationToken.None);
        await store.SaveAsync(WriteTempFile("new"), "app-20260808-140000000.db", CancellationToken.None);

        var bytes = await store.ReadNewestAsync();

        Assert.NotNull(bytes);
        Assert.Equal("new", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public async Task ReadNewest_EmptyStore_ReturnsNull()
    {
        Assert.Null(await Store().ReadNewestAsync());
    }

    [Fact]
    public async Task List_ReportsNewestFirstWithSizeAndTimestamp()
    {
        var store = Store();
        await store.SaveAsync(WriteTempFile("old"), "app-20260808-120000000.db", CancellationToken.None);
        await store.SaveAsync(WriteTempFile("newer"), "app-20260808-140000000.db", CancellationToken.None);

        var infos = await store.ListAsync(CancellationToken.None);

        Assert.Equal(["app-20260808-140000000.db", "app-20260808-120000000.db"], infos.Select(i => i.Name));
        Assert.Equal(5, infos[0].SizeBytes);
        Assert.Equal(new DateTime(2026, 8, 8, 14, 0, 0, DateTimeKind.Utc), infos[0].CreatedAt);
    }

    // A database whose own name contains dashes must not confuse the timestamp parse.
    [Fact]
    public async Task List_ParsesTheTimestampWhenTheStemContainsDashes()
    {
        var store = Store();
        await store.SaveAsync(WriteTempFile("x"), "my-app-db-20260808-140000000.db", CancellationToken.None);

        var infos = await store.ListAsync(CancellationToken.None);

        Assert.Equal(new DateTime(2026, 8, 8, 14, 0, 0, DateTimeKind.Utc), infos[0].CreatedAt);
    }

    [Fact]
    public async Task List_UnparseableName_StillLists()
    {
        var store = Store();
        await store.SaveAsync(WriteTempFile("x"), "not-a-timestamp.db", CancellationToken.None);

        var infos = await store.ListAsync(CancellationToken.None);

        Assert.Equal("not-a-timestamp.db", Assert.Single(infos).Name);
    }
}
