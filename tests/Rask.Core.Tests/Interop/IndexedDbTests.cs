using Rask.Core.Browser;

namespace Rask.Core.Tests.Interop;

public class IndexedDbTests
{
    [Fact]
    public async Task Asking_whether_IndexedDB_is_supported_calls_the_helper()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskIdb.isSupported", true);

        Assert.True(await new IndexedDb(js).IsSupported());
    }

    [Fact]
    public async Task Opening_a_store_opens_the_named_database()
    {
        var js = new FakeJsRuntime();

        var store = await new IndexedDb(js).OpenStore("cache");

        Assert.NotNull(store);
        Assert.Equal(["cache"], js.ArgsFor("__raskIdb.open"));
    }

    [Fact]
    public async Task Setting_passes_the_store_key_and_value()
    {
        var js = new FakeJsRuntime();
        var store = await new IndexedDb(js).OpenStore("cache");

        await store.Set("greeting", "hello");

        Assert.Equal(["cache", "greeting", "hello"], js.ArgsFor("__raskIdb.set"));
    }

    [Fact]
    public async Task Getting_passes_the_store_and_key_and_gives_the_value()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskIdb.get", "hello");
        var store = await new IndexedDb(js).OpenStore("cache");

        Assert.Equal("hello", await store.Get("greeting"));
        Assert.Equal(["cache", "greeting"], js.ArgsFor("__raskIdb.get"));
    }

    // 250 is deliberately outside ASCII: it catches a helper that round-trips through a text encoding
    // instead of treating the payload as bytes.
    private static readonly byte[] Sample = [1, 2, 250];

    [Fact]
    public async Task Setting_bytes_sends_base64_to_the_binary_helper()
    {
        var js = new FakeJsRuntime();
        var store = await new IndexedDb(js).OpenStore("files");

        await store.SetBytes("db", Sample);

        // The binary helper, not `set` — that is what decodes to a Uint8Array so the bytes cost
        // their own size in quota rather than their base64 inflation.
        Assert.Equal(["files", "db", Convert.ToBase64String(Sample)], js.ArgsFor("__raskIdb.setBytes"));
    }

    [Fact]
    public async Task Getting_bytes_decodes_base64()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskIdb.getBytes", Convert.ToBase64String(Sample));
        var store = await new IndexedDb(js).OpenStore("files");

        var bytes = await store.GetBytes("db");

        Assert.NotNull(bytes);
        Assert.Equal(Sample, bytes);
        Assert.Equal(["files", "db"], js.ArgsFor("__raskIdb.getBytes"));
    }

    [Fact]
    public async Task Getting_bytes_for_an_absent_key_gives_null()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskIdb.getBytes", (string?)null);
        var store = await new IndexedDb(js).OpenStore("files");

        Assert.Null(await store.GetBytes("missing"));
    }

    [Fact]
    public async Task Setting_an_empty_byte_array_round_trips_as_empty_not_null()
    {
        var js = new FakeJsRuntime();
        var store = await new IndexedDb(js).OpenStore("files");

        await store.SetBytes("empty", []);

        // "" is a valid base64 payload; it must not be confused with an absent key on the way back.
        Assert.Equal(["files", "empty", ""], js.ArgsFor("__raskIdb.setBytes"));
        js.SetResponse("__raskIdb.getBytes", "");

        var bytes = await store.GetBytes("empty");

        Assert.NotNull(bytes);
        Assert.Empty(bytes);
    }

    // The default interface implementation is what a store written before these methods existed gets.
    // It must stay correct — just larger — rather than throwing. Held through the interface on purpose:
    // a default implementation is only reachable that way, which is exactly how callers will hit it.
    [Fact]
    public async Task The_default_implementation_of_setting_bytes_falls_back_to_the_string_api()
    {
        var backing = new StringOnlyStore();
        IKeyValueStore store = backing;

        await store.SetBytes("db", Sample);

        Assert.Equal(Convert.ToBase64String(Sample), backing.Values["db"]);

        var bytes = await store.GetBytes("db");

        Assert.NotNull(bytes);
        Assert.Equal(Sample, bytes);
        Assert.Null(await store.GetBytes("missing"));
    }

    [Fact]
    public async Task The_byte_methods_throw_for_null_args()
    {
        var store = await new IndexedDb(new FakeJsRuntime()).OpenStore("files");

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await store.SetBytes(null!, []));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await store.SetBytes("k", null!));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await store.GetBytes(null!));
    }

    private sealed class StringOnlyStore : IKeyValueStore
    {
        public Dictionary<string, string> Values { get; } = [];

        public ValueTask Set(string key, string value)
        {
            Values[key] = value;
            return ValueTask.CompletedTask;
        }

        public ValueTask<string?> Get(string key) =>
            ValueTask.FromResult(Values.TryGetValue(key, out var value) ? value : null);

        public ValueTask Delete(string key)
        {
            Values.Remove(key);
            return ValueTask.CompletedTask;
        }

        public ValueTask<string[]> Keys() => ValueTask.FromResult(Values.Keys.ToArray());

        public ValueTask Clear()
        {
            Values.Clear();
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task Listing_keys_gives_the_key_array()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskIdb.keys", new[] { "a", "b" });
        var store = await new IndexedDb(js).OpenStore("cache");

        Assert.Equal(new[] { "a", "b" }, await store.Keys());
        Assert.Equal(["cache"], js.ArgsFor("__raskIdb.keys"));
    }

    [Fact]
    public async Task Delete_and_clear_call_their_helpers()
    {
        var js = new FakeJsRuntime();
        var store = await new IndexedDb(js).OpenStore("cache");

        await store.Delete("greeting");
        await store.Clear();

        Assert.Equal(["cache", "greeting"], js.ArgsFor("__raskIdb.delete"));
        Assert.Equal(["cache"], js.ArgsFor("__raskIdb.clear"));
    }

    [Fact]
    public async Task Null_args_throw()
    {
        var db = new IndexedDb(new FakeJsRuntime());
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await db.OpenStore(null!));
        var store = await db.OpenStore("cache");

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await store.Set(null!, "v"));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await store.Set("k", null!));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await store.Get(null!));
    }
}
