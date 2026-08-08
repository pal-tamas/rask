using Rask.Core.Browser;

namespace Rask.Core.Tests.Interop;

public class IndexedDbTests
{
    [Fact]
    public async Task IsSupported_CallsHelper()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskIdb.isSupported", true);

        Assert.True(await new IndexedDb(js).IsSupportedAsync());
    }

    [Fact]
    public async Task OpenStore_OpensNamedDatabase()
    {
        var js = new FakeJsRuntime();

        var store = await new IndexedDb(js).OpenStoreAsync("cache");

        Assert.NotNull(store);
        Assert.Equal(["cache"], js.ArgsFor("__raskIdb.open"));
    }

    [Fact]
    public async Task Set_PassesStoreKeyValue()
    {
        var js = new FakeJsRuntime();
        var store = await new IndexedDb(js).OpenStoreAsync("cache");

        await store.SetAsync("greeting", "hello");

        Assert.Equal(["cache", "greeting", "hello"], js.ArgsFor("__raskIdb.set"));
    }

    [Fact]
    public async Task Get_PassesStoreKey_AndReturnsValue()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskIdb.get", "hello");
        var store = await new IndexedDb(js).OpenStoreAsync("cache");

        Assert.Equal("hello", await store.GetAsync("greeting"));
        Assert.Equal(["cache", "greeting"], js.ArgsFor("__raskIdb.get"));
    }

    // 250 is deliberately outside ASCII: it catches a helper that round-trips through a text encoding
    // instead of treating the payload as bytes.
    private static readonly byte[] Sample = [1, 2, 250];

    [Fact]
    public async Task SetBytes_SendsBase64_ToTheBinaryHelper()
    {
        var js = new FakeJsRuntime();
        var store = await new IndexedDb(js).OpenStoreAsync("files");

        await store.SetBytesAsync("db", Sample);

        // The binary helper, not `set` — that is what decodes to a Uint8Array so the bytes cost
        // their own size in quota rather than their base64 inflation.
        Assert.Equal(["files", "db", Convert.ToBase64String(Sample)], js.ArgsFor("__raskIdb.setBytes"));
    }

    [Fact]
    public async Task GetBytes_DecodesBase64()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskIdb.getBytes", Convert.ToBase64String(Sample));
        var store = await new IndexedDb(js).OpenStoreAsync("files");

        var bytes = await store.GetBytesAsync("db");

        Assert.NotNull(bytes);
        Assert.Equal(Sample, bytes);
        Assert.Equal(["files", "db"], js.ArgsFor("__raskIdb.getBytes"));
    }

    [Fact]
    public async Task GetBytes_AbsentKey_ReturnsNull()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskIdb.getBytes", (string?)null);
        var store = await new IndexedDb(js).OpenStoreAsync("files");

        Assert.Null(await store.GetBytesAsync("missing"));
    }

    [Fact]
    public async Task SetBytes_EmptyArray_RoundTripsAsEmptyNotNull()
    {
        var js = new FakeJsRuntime();
        var store = await new IndexedDb(js).OpenStoreAsync("files");

        await store.SetBytesAsync("empty", []);

        // "" is a valid base64 payload; it must not be confused with an absent key on the way back.
        Assert.Equal(["files", "empty", ""], js.ArgsFor("__raskIdb.setBytes"));
        js.SetResponse("__raskIdb.getBytes", "");

        var bytes = await store.GetBytesAsync("empty");

        Assert.NotNull(bytes);
        Assert.Empty(bytes);
    }

    // The default interface implementation is what a store written before these methods existed gets.
    // It must stay correct — just larger — rather than throwing. Held through the interface on purpose:
    // a default implementation is only reachable that way, which is exactly how callers will hit it.
    [Fact]
    public async Task SetBytes_DefaultImplementation_FallsBackToTheStringApi()
    {
        var backing = new StringOnlyStore();
        IKeyValueStore store = backing;

        await store.SetBytesAsync("db", Sample);

        Assert.Equal(Convert.ToBase64String(Sample), backing.Values["db"]);

        var bytes = await store.GetBytesAsync("db");

        Assert.NotNull(bytes);
        Assert.Equal(Sample, bytes);
        Assert.Null(await store.GetBytesAsync("missing"));
    }

    [Fact]
    public async Task Bytes_NullArgs_Throw()
    {
        var store = await new IndexedDb(new FakeJsRuntime()).OpenStoreAsync("files");

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await store.SetBytesAsync(null!, []));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await store.SetBytesAsync("k", null!));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await store.GetBytesAsync(null!));
    }

    private sealed class StringOnlyStore : IKeyValueStore
    {
        public Dictionary<string, string> Values { get; } = [];

        public ValueTask SetAsync(string key, string value)
        {
            Values[key] = value;
            return ValueTask.CompletedTask;
        }

        public ValueTask<string?> GetAsync(string key) =>
            ValueTask.FromResult(Values.TryGetValue(key, out var value) ? value : null);

        public ValueTask DeleteAsync(string key)
        {
            Values.Remove(key);
            return ValueTask.CompletedTask;
        }

        public ValueTask<string[]> KeysAsync() => ValueTask.FromResult(Values.Keys.ToArray());

        public ValueTask ClearAsync()
        {
            Values.Clear();
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task Keys_ReturnsKeyArray()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskIdb.keys", new[] { "a", "b" });
        var store = await new IndexedDb(js).OpenStoreAsync("cache");

        Assert.Equal(new[] { "a", "b" }, await store.KeysAsync());
        Assert.Equal(["cache"], js.ArgsFor("__raskIdb.keys"));
    }

    [Fact]
    public async Task DeleteAndClear_CallHelpers()
    {
        var js = new FakeJsRuntime();
        var store = await new IndexedDb(js).OpenStoreAsync("cache");

        await store.DeleteAsync("greeting");
        await store.ClearAsync();

        Assert.Equal(["cache", "greeting"], js.ArgsFor("__raskIdb.delete"));
        Assert.Equal(["cache"], js.ArgsFor("__raskIdb.clear"));
    }

    [Fact]
    public async Task NullArgs_Throw()
    {
        var db = new IndexedDb(new FakeJsRuntime());
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await db.OpenStoreAsync(null!));
        var store = await db.OpenStoreAsync("cache");
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await store.SetAsync(null!, "v"));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await store.SetAsync("k", null!));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await store.GetAsync(null!));
    }
}
