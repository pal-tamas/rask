using Microsoft.EntityFrameworkCore;
using Rask.Data;

namespace Rask.Storage.Tests;

/// <summary>
///     A file saved inside <c>Db.Transaction</c> takes part in it: in the database its rows commit with the work, and on
///     a disk its bytes are written at once and removed again when the work rolls back. A delete waits for the commit.
/// </summary>
[Collection(StorageDbCollection.Name)]
public sealed class StorageDbTransactionTests : IDisposable
{
    private static readonly Action<StorageOptions> InTheDatabase = o => o.Provider = StorageProvider.Database;

    public void Dispose() => Db.Reset();

    [Fact]
    public async Task A_file_saved_to_the_database_in_a_rolled_back_transaction_leaves_no_row_and_no_bytes()
    {
        await using var harness = new StorageHarness(InTheDatabase);
        Db.Configure(harness.NewContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Db.Transaction(async () =>
        {
            await harness.Files.Save(new MemoryStream(Samples.Png()), "scan.png", TestContext.Current.CancellationToken);
            throw new InvalidOperationException("the return was refused");
        }, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(0, await harness.CountRowsAsync());
        Assert.Equal(0, await BytesRowsAsync(harness));
    }

    [Fact]
    public async Task A_file_saved_to_the_database_in_a_transaction_is_seen_only_once_it_commits()
    {
        await using var harness = new StorageHarness(InTheDatabase);
        Db.Configure(harness.NewContext);
        var seenBeforeCommit = -1;

        await Db.Transaction(async () =>
        {
            await harness.Files.Save(new MemoryStream(Samples.Png()), "scan.png", TestContext.Current.CancellationToken);
            seenBeforeCommit = await harness.CountRowsAsync();
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, seenBeforeCommit);
        Assert.Equal(1, await harness.CountRowsAsync());
        Assert.Equal(1, await BytesRowsAsync(harness));
    }

    [Fact]
    public async Task A_file_saved_to_disk_in_a_rolled_back_transaction_has_its_bytes_deleted()
    {
        await using var harness = new StorageHarness();
        Db.Configure(harness.NewContext);
        var writtenBeforeRollback = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Db.Transaction(async () =>
        {
            await harness.Files.Save(new MemoryStream(Samples.Png()), "scan.png", TestContext.Current.CancellationToken);
            writtenBeforeRollback = harness.StoredPaths().Length;
            throw new InvalidOperationException("the return was refused");
        }, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(1, writtenBeforeRollback);
        Assert.Empty(harness.StoredPaths());
        Assert.Equal(0, await harness.CountRowsAsync());
    }

    [Fact]
    public async Task A_file_saved_to_disk_in_a_committed_transaction_keeps_its_bytes_and_its_row()
    {
        await using var harness = new StorageHarness();
        Db.Configure(harness.NewContext);
        var seenBeforeCommit = -1;

        var file = await Db.Transaction(async () =>
        {
            var saved = await harness.Files.Save(new MemoryStream(Samples.Png()), "scan.png", TestContext.Current.CancellationToken);
            seenBeforeCommit = await harness.CountRowsAsync();
            return saved;
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, seenBeforeCommit);
        Assert.Single(harness.StoredPaths());
        Assert.NotNull(await harness.Files.Get(file.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_file_deleted_in_a_transaction_keeps_its_bytes_until_the_commit()
    {
        await using var harness = new StorageHarness();
        Db.Configure(harness.NewContext);
        var file = await harness.Files.Save(new MemoryStream(Samples.Png()), "scan.png", TestContext.Current.CancellationToken);
        var bytesBeforeCommit = 0;

        await Db.Transaction(async () =>
        {
            await harness.Files.Delete(file.Id, TestContext.Current.CancellationToken);
            bytesBeforeCommit = harness.StoredPaths().Length;
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, bytesBeforeCommit);
        Assert.Empty(harness.StoredPaths());
        Assert.Equal(0, await harness.CountRowsAsync());
    }

    [Fact]
    public async Task A_file_deleted_in_a_rolled_back_transaction_keeps_its_row_and_its_bytes()
    {
        await using var harness = new StorageHarness(InTheDatabase);
        Db.Configure(harness.NewContext);
        var file = await harness.Files.Save(new MemoryStream(Samples.Png()), "scan.png", TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Db.Transaction(async () =>
        {
            await harness.Files.Delete(file.Id, TestContext.Current.CancellationToken);
            throw new InvalidOperationException("the return was refused");
        }, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(1, await harness.CountRowsAsync());
        Assert.Equal(1, await BytesRowsAsync(harness));
    }

    [Fact]
    public async Task A_storage_table_on_another_database_is_refused_inside_a_transaction_and_writes_nothing()
    {
        await using var harness = new StorageHarness();
        await using var elsewhere = new StorageHarness();
        Db.Configure(elsewhere.NewContext);

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => Db.Transaction(async () =>
            await harness.Files.Save(new MemoryStream(Samples.Png()), "scan.png", TestContext.Current.CancellationToken),
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("another database", refused.Message, StringComparison.Ordinal);
        Assert.Empty(harness.StoredPaths());
        Assert.Equal(0, await harness.CountRowsAsync());
    }

    [Fact]
    public async Task Outside_a_transaction_a_save_is_seen_at_once_and_a_delete_removes_the_bytes_at_once()
    {
        await using var harness = new StorageHarness();

        var file = await harness.Files.Save(new MemoryStream(Samples.Png()), "scan.png", TestContext.Current.CancellationToken);
        var rowsAfterSave = await harness.CountRowsAsync();
        await harness.Files.Delete(file.Id, TestContext.Current.CancellationToken);

        Assert.Equal(1, rowsAfterSave);
        Assert.Empty(harness.StoredPaths());
        Assert.Equal(0, await harness.CountRowsAsync());
    }

    private static async Task<int> BytesRowsAsync(StorageHarness harness)
    {
        await using var db = harness.NewContext();
        return await db.Set<StoredFileBytes>().CountAsync(TestContext.Current.CancellationToken);
    }
}
