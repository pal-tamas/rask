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
