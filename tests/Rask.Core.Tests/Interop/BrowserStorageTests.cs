using Rask.Core.Browser;

namespace Rask.Core.Tests.Interop;

public class BrowserStorageTests
{
    [Fact]
    public async Task Setting_local_storage_sends_setItem_with_the_key_and_value()
    {
        var js = new FakeJsRuntime();
        var storage = new BrowserStorage(js);

        await storage.Local.SetAsync("theme", "dark");

        Assert.Equal(["theme", "dark"], js.ArgsFor("localStorage.setItem"));
    }

    [Fact]
    public async Task Setting_session_storage_targets_session_storage_not_local()
    {
        var js = new FakeJsRuntime();
        var storage = new BrowserStorage(js);

        await storage.Session.SetAsync("k", "v");

        Assert.Equal(1, js.CallCount("sessionStorage.setItem"));
        Assert.Equal(0, js.CallCount("localStorage.setItem"));
    }

    [Fact]
    public async Task Getting_local_storage_sends_getItem_and_gives_the_canned_value()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("localStorage.getItem", "dark");
        var storage = new BrowserStorage(js);

        var value = await storage.Local.GetAsync("theme");

        Assert.Equal("dark", value);
        Assert.Equal(["theme"], js.ArgsFor("localStorage.getItem"));
    }

    [Fact]
    public async Task Remove_clear_key_and_length_use_the_expected_identifiers()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("localStorage.length", 3);
        var storage = new BrowserStorage(js);

        await storage.Local.RemoveAsync("k");
        await storage.Local.ClearAsync();
        await storage.Local.KeyAsync(2);
        var length = await storage.Local.LengthAsync();

        Assert.Equal(["k"], js.ArgsFor("localStorage.removeItem"));
        Assert.Empty(js.ArgsFor("localStorage.clear")!);
        Assert.Equal([2], js.ArgsFor("localStorage.key"));
        Assert.Equal(3, length);
    }

    [Fact]
    public async Task A_null_key_or_value_throws()
    {
        var storage = new BrowserStorage(new FakeJsRuntime());

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await storage.Local.GetAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await storage.Local.SetAsync("k", null!));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await storage.Local.SetAsync(null!, "v"));
    }
}
