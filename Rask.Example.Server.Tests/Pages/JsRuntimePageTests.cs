using System.Reflection;
using Rask.Core;
using Rask.Example.Shared.Pages;
using Rask.Example.Shared.Tests.Infrastructure;

namespace Rask.Example.Server.Tests.Pages;

// Originally test for Server-only JsRuntimePage; the page has been unified into
// Rask.Example.Shared.Pages so it now runs on Server + WASM. The unit tests
// stay on the Server-Tests project since they don't depend on host transport.

public sealed class JsRuntimePageTests
{
    [Fact]
    public void Head_TitleSet()
    {
        var head = typeof(JsRuntimePage).GetProperty("Head",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var js = new FakeJsRuntime();
        var page = new JsRuntimePage(js);
        var headComponent = (Component?)head.GetValue(page);
        Assert.NotNull(headComponent);
        Assert.Contains("IJSRuntime", headComponent!.ToHtml());
    }

    [Fact]
    public async Task OnRenderedAsync_FirstRender_ReadsSessionStorage_PopulatesLastRead()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("sessionStorage.getItem", "stored-value");
        var page = new JsRuntimePage(js);

        await InvokeOnRenderedAsync(page, firstRender: true);

        Assert.Equal("stored-value", GetField<string?>(page, "_lastRead"));
        Assert.Equal("Read on mount: stored-value", GetField<string?>(page, "_status"));
    }

    [Fact]
    public async Task OnRenderedAsync_FirstRender_NullStored_StatusShowsNoValueHint()
    {
        var js = new FakeJsRuntime();
        // No SetResponse → returns default (null for string?).
        var page = new JsRuntimePage(js);

        await InvokeOnRenderedAsync(page, firstRender: true);

        Assert.Null(GetField<string?>(page, "_lastRead"));
        Assert.Equal("(no value yet — try Set)", GetField<string?>(page, "_status"));
    }

    [Fact]
    public async Task OnRenderedAsync_NonFirstRender_NoOp()
    {
        var js = new FakeJsRuntime();
        var page = new JsRuntimePage(js);

        await InvokeOnRenderedAsync(page, firstRender: false);

        // No call should have been made to sessionStorage.getItem.
        Assert.Equal(0, js.CallCount("sessionStorage.getItem"));
        Assert.Null(GetField<string?>(page, "_status"));
    }

    [Fact]
    public async Task OnRenderedAsync_JsThrows_SetsStatusReadFailed()
    {
        var js = new FakeJsRuntime();
        js.SetException("sessionStorage.getItem", new InvalidOperationException("boom"));
        var page = new JsRuntimePage(js);

        await InvokeOnRenderedAsync(page, firstRender: true);

        var status = GetField<string?>(page, "_status");
        Assert.NotNull(status);
        Assert.StartsWith("Read failed:", status);
        Assert.Contains("boom", status);
    }

    [Fact]
    public async Task SetAsync_InvokesSetItem_UpdatesStatus()
    {
        var js = new FakeJsRuntime();
        var page = new JsRuntimePage(js);
        SetField(page, "_input", "hello");

        await InvokePrivate(page, "SetAsync");

        Assert.Equal(1, js.CallCount("sessionStorage.setItem"));
        Assert.Equal("Set to: hello", GetField<string?>(page, "_status"));
    }

    [Fact]
    public async Task SetAsync_ThrowingJs_SetsStatusSetFailed()
    {
        var js = new FakeJsRuntime();
        js.SetException("sessionStorage.setItem", new InvalidOperationException("nope"));
        var page = new JsRuntimePage(js);
        SetField(page, "_input", "x");

        await InvokePrivate(page, "SetAsync");

        var status = GetField<string?>(page, "_status");
        Assert.StartsWith("Set failed:", status);
    }

    [Fact]
    public async Task ReadAsync_PopulatesLastRead_AndStatus()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("sessionStorage.getItem", "read-back");
        var page = new JsRuntimePage(js);

        await InvokePrivate(page, "ReadAsync");

        Assert.Equal("read-back", GetField<string?>(page, "_lastRead"));
        Assert.Equal("Read: read-back", GetField<string?>(page, "_status"));
    }

    [Fact]
    public async Task ReadAsync_NullStored_SetsStatusReadNull()
    {
        var js = new FakeJsRuntime();
        var page = new JsRuntimePage(js);

        await InvokePrivate(page, "ReadAsync");

        Assert.Equal("Read: (null)", GetField<string?>(page, "_status"));
    }

    [Fact]
    public async Task ReadAsync_ThrowingJs_SetsStatusReadFailed()
    {
        var js = new FakeJsRuntime();
        js.SetException("sessionStorage.getItem", new InvalidOperationException("boom"));
        var page = new JsRuntimePage(js);

        await InvokePrivate(page, "ReadAsync");

        var status = GetField<string?>(page, "_status");
        Assert.StartsWith("Read failed:", status);
    }

    [Fact]
    public async Task RemoveAsync_InvokesRemoveItem_ClearsLastRead()
    {
        var js = new FakeJsRuntime();
        var page = new JsRuntimePage(js);
        SetField(page, "_lastRead", "previous");

        await InvokePrivate(page, "RemoveAsync");

        Assert.Equal(1, js.CallCount("sessionStorage.removeItem"));
        Assert.Null(GetField<string?>(page, "_lastRead"));
        Assert.Equal("Removed", GetField<string?>(page, "_status"));
    }

    [Fact]
    public async Task RemoveAsync_ThrowingJs_SetsStatusRemoveFailed()
    {
        var js = new FakeJsRuntime();
        js.SetException("sessionStorage.removeItem", new InvalidOperationException("nope"));
        var page = new JsRuntimePage(js);

        await InvokePrivate(page, "RemoveAsync");

        var status = GetField<string?>(page, "_status");
        Assert.StartsWith("Remove failed:", status);
    }

    [Fact]
    public void RouteAttribute_RegisteredAt_Jsruntime()
    {
        var attr = typeof(JsRuntimePage)
            .GetCustomAttribute<Rask.Core.Routing.RouteAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("jsruntime", attr!.Template);
    }

    private static async Task InvokeOnRenderedAsync(JsRuntimePage page, bool firstRender)
    {
        var mi = typeof(JsRuntimePage).GetMethod("OnRenderedAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)mi.Invoke(page, [firstRender])!;
    }

    private static async Task InvokePrivate(JsRuntimePage page, string name)
    {
        var mi = typeof(JsRuntimePage).GetMethod(name,
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)mi.Invoke(page, null)!;
    }

    private static T GetField<T>(JsRuntimePage page, string name)
    {
        var f = typeof(JsRuntimePage).GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var v = f.GetValue(page);
        return v is null ? default! : (T)v;
    }

    private static void SetField(JsRuntimePage page, string name, object? value)
    {
        var f = typeof(JsRuntimePage).GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        f.SetValue(page, value);
    }
}
