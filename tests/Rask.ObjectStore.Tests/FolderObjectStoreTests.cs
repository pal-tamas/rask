using System.Text;

namespace Rask.ObjectStore.Tests;

/// <summary>
///     The contract the sync engines rely on, asserted against a real directory: ordinal key order,
///     exclusive <c>startAfter</c>, grouped listing, atomic conditional create — plus the traversal
///     guard, because keys can come from a listing of a folder somebody else also writes to.
/// </summary>
public sealed class FolderObjectStoreTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"rask-folder-store-{Guid.NewGuid():N}");

    private FolderObjectStore Store => new(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder is not worth failing a green run over.
        }
    }

    [Fact]
    public async Task An_object_round_trips()
    {
        var store = Store;
        await store.PutAsync("a/b/c.json", Encoding.UTF8.GetBytes("hello"));

        var stream = await store.OpenReadAsync("a/b/c.json");
        Assert.NotNull(stream);

        using var reader = new StreamReader(stream!);
        Assert.Equal("hello", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task A_missing_object_reads_as_null()
    {
        Assert.Null(await Store.OpenReadAsync("nope.json"));
        Assert.Null(await Store.GetRangeAsync("nope.json", 0, 10));
    }

    [Fact]
    public async Task Keys_list_in_ordinal_order()
    {
        // Forward-only reading is built on this: the engine treats "the last key I read" as a position
        // in a total order, so a store that listed by mtime or by directory order would skip changes.
        var store = Store;
        foreach (var key in new[] { "p/0000000000000010.json", "p/0000000000000002.json", "p/0000000000000001.json" })
        {
            await store.PutAsync(key, [1]);
        }

        var listed = await store.ListAsync("p/");

        Assert.Equal(
            ["p/0000000000000001.json", "p/0000000000000002.json", "p/0000000000000010.json"],
            listed.Select(e => e.Key));
    }

    [Fact]
    public async Task StartAfter_is_exclusive()
    {
        var store = Store;
        await store.PutAsync("p/a.json", [1]);
        await store.PutAsync("p/b.json", [1]);
        await store.PutAsync("p/c.json", [1]);

        var listed = await store.ListAsync("p/", "p/b.json");

        Assert.Equal(["p/c.json"], listed.Select(e => e.Key));
    }

    [Fact]
    public async Task A_prefix_only_matches_its_own_keys()
    {
        var store = Store;
        await store.PutAsync("alice/x.json", [1]);
        await store.PutAsync("bob/x.json", [1]);

        Assert.Equal(["alice/x.json"], (await store.ListAsync("alice/")).Select(e => e.Key));
    }

    [Fact]
    public async Task Grouped_listing_returns_the_immediate_folders()
    {
        var store = Store;
        await store.PutAsync("crdt/alice/changes/1.json", [1]);
        await store.PutAsync("crdt/alice/changes/2.json", [1]);
        await store.PutAsync("crdt/bob/changes/1.json", [1]);

        var prefixes = await store.ListPrefixesAsync("crdt/");

        Assert.Equal(["crdt/alice/", "crdt/bob/"], prefixes);
    }

    [Fact]
    public async Task Conditional_create_refuses_an_existing_key()
    {
        var store = Store;

        Assert.True(await store.TryCreateAsync("once.json", Encoding.UTF8.GetBytes("first")));
        Assert.False(await store.TryCreateAsync("once.json", Encoding.UTF8.GetBytes("second")));

        var stream = await store.OpenReadAsync("once.json");
        using var reader = new StreamReader(stream!);
        Assert.Equal("first", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task A_ranged_read_returns_the_range()
    {
        var store = Store;
        await store.PutAsync("r.bin", [0, 1, 2, 3, 4, 5]);

        Assert.Equal([2, 3], await store.GetRangeAsync("r.bin", 2, 2));
        Assert.Equal([4, 5], await store.GetRangeAsync("r.bin", 4, 99));
        Assert.Empty((await store.GetRangeAsync("r.bin", 99, 2))!);
    }

    [Fact]
    public async Task Overwriting_replaces_the_whole_object()
    {
        var store = Store;
        await store.PutAsync("k.bin", [1, 2, 3, 4, 5]);
        await store.PutAsync("k.bin", [9]);

        Assert.Equal([9], await store.GetRangeAsync("k.bin", 0, 100));
    }

    [Fact]
    public async Task A_half_written_object_is_not_listed()
    {
        // The folder may be replicated by something else while it is being written, so the temporary
        // file must never look like an object to a reader.
        var store = Store;
        await store.PutAsync("p/real.json", [1]);
        await File.WriteAllBytesAsync(Path.Combine(_root, "p", "pending.json.tmp"), [1]);

        Assert.Equal(["p/real.json"], (await store.ListAsync("p/")).Select(e => e.Key));
    }

    [Theory]
    [InlineData("../escape.json")]
    [InlineData("a/../../escape.json")]
    public async Task A_key_that_climbs_out_of_the_folder_is_refused(string key)
    {
        // Keys are not always this process's own strings — a peer's prefix comes back from a listing —
        // so escaping is refused rather than normalised into something readable.
        await Assert.ThrowsAsync<ArgumentException>(() => Store.PutAsync(key, [1]));
        await Assert.ThrowsAsync<ArgumentException>(() => Store.OpenReadAsync(key));
    }

    [Fact]
    public async Task An_absolute_path_is_refused()
    {
        var absolute = Path.Combine(Path.GetTempPath(), "elsewhere.json");
        await Assert.ThrowsAsync<ArgumentException>(() => Store.PutAsync(absolute, [1]));
    }

    [Fact]
    public async Task Deleting_something_absent_is_not_an_error()
    {
        await Store.DeleteAsync("never-existed.json");
    }

    [Fact]
    public async Task Delete_removes_the_object()
    {
        var store = Store;
        await store.PutAsync("gone.json", [1]);
        await store.DeleteAsync("gone.json");

        Assert.Null(await store.OpenReadAsync("gone.json"));
    }

    [Fact]
    public async Task A_second_store_over_the_same_folder_sees_the_same_objects()
    {
        // The whole point when the folder is shared: two processes, or two runs, are the same bucket.
        await Store.PutAsync("shared/x.json", Encoding.UTF8.GetBytes("hi"));

        Assert.Equal(["shared/x.json"], (await Store.ListAsync("shared/")).Select(e => e.Key));
    }
}
