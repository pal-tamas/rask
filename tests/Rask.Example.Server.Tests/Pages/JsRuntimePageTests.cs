using System.Reflection;
using Rask.Core.Routing;
using Rask.Example.Shared.Features;
using Rask.Example.Shared.Tests.Infrastructure;

namespace Rask.Example.Server.Tests.Pages;

// The JsRuntime feature is split into a host page (JsRuntimePage: route + Head + CodeSample)
// and the interactive demo (JsRuntimeDemo: the IJSRuntime sessionStorage round-trip). The page
// is a parameterless host; the demo takes IJSRuntime through its ctor. Page-level tests target
// JsRuntimePage; the behavioral tests target JsRuntimeDemo. Both run on Server + WASM, so the
// unit tests stay on the Server-Tests project since they don't depend on host transport.

public sealed class JsRuntimePageTests
{
    [Fact]
    public void Head_TitleSet()
    {
        var head = typeof(JsRuntimePage).GetProperty("Head",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var page = new JsRuntimePage();
        // Head now returns a RenderResult struct; unwrap to the contributed Component.
        var headComponent = ((RenderResult)head.GetValue(page)!).ToComponentOrNull();
        Assert.NotNull(headComponent);
        Assert.Contains("IJSRuntime", headComponent!.ToHtml());
    }

    [Fact]
    public async Task OnRenderedAsync_FirstRender_ReadsSessionStorage_PopulatesLastRead()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("sessionStorage.getItem", "stored-value");
        var demo = new JsRuntimeDemo(js);

        await InvokeOnRenderedAsync(demo, true);

        Assert.Equal("stored-value", GetField<string?>(demo, "_lastRead"));
        Assert.Equal("Read on mount: stored-value", GetField<string?>(demo, "_status"));
    }

    [Fact]
    public async Task OnRenderedAsync_FirstRender_NullStored_StatusShowsNoValueHint()
    {
        var js = new FakeJsRuntime();
        // No SetResponse → returns default (null for string?).
        var demo = new JsRuntimeDemo(js);

        await InvokeOnRenderedAsync(demo, true);

        Assert.Null(GetField<string?>(demo, "_lastRead"));
        Assert.Equal("(no value yet — try Set)", GetField<string?>(demo, "_status"));
    }

    [Fact]
    public async Task OnRenderedAsync_NonFirstRender_NoOp()
    {
        var js = new FakeJsRuntime();
        var demo = new JsRuntimeDemo(js);

        await InvokeOnRenderedAsync(demo, false);

        // No call should have been made to sessionStorage.getItem.
        Assert.Equal(0, js.CallCount("sessionStorage.getItem"));
        Assert.Null(GetField<string?>(demo, "_status"));
    }

    [Fact]
    public async Task OnRenderedAsync_JsThrows_SetsStatusReadFailed()
    {
        var js = new FakeJsRuntime();
        js.SetException("sessionStorage.getItem", new InvalidOperationException("boom"));
        var demo = new JsRuntimeDemo(js);

        await InvokeOnRenderedAsync(demo, true);

        var status = GetField<string?>(demo, "_status");
        Assert.NotNull(status);
        Assert.StartsWith("Read failed:", status);
        Assert.Contains("boom", status);
    }

    [Fact]
    public async Task SetAsync_InvokesSetItem_UpdatesStatus()
    {
        var js = new FakeJsRuntime();
        var demo = new JsRuntimeDemo(js);
        SetField(demo, "_input", "hello");

        await InvokePrivate(demo, "SetAsync");

        Assert.Equal(1, js.CallCount("sessionStorage.setItem"));
        Assert.Equal("Set to: hello", GetField<string?>(demo, "_status"));
    }

    [Fact]
    public async Task SetAsync_ThrowingJs_SetsStatusSetFailed()
    {
        var js = new FakeJsRuntime();
        js.SetException("sessionStorage.setItem", new InvalidOperationException("nope"));
        var demo = new JsRuntimeDemo(js);
        SetField(demo, "_input", "x");

        await InvokePrivate(demo, "SetAsync");

        var status = GetField<string?>(demo, "_status");
        Assert.StartsWith("Set failed:", status);
    }

    [Fact]
    public async Task ReadAsync_PopulatesLastRead_AndStatus()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("sessionStorage.getItem", "read-back");
        var demo = new JsRuntimeDemo(js);

        await InvokePrivate(demo, "ReadAsync");

        Assert.Equal("read-back", GetField<string?>(demo, "_lastRead"));
        Assert.Equal("Read: read-back", GetField<string?>(demo, "_status"));
    }

    [Fact]
    public async Task ReadAsync_NullStored_SetsStatusReadNull()
    {
        var js = new FakeJsRuntime();
        var demo = new JsRuntimeDemo(js);

        await InvokePrivate(demo, "ReadAsync");

        Assert.Equal("Read: (null)", GetField<string?>(demo, "_status"));
    }

    [Fact]
    public async Task ReadAsync_ThrowingJs_SetsStatusReadFailed()
    {
        var js = new FakeJsRuntime();
        js.SetException("sessionStorage.getItem", new InvalidOperationException("boom"));
        var demo = new JsRuntimeDemo(js);

        await InvokePrivate(demo, "ReadAsync");

        var status = GetField<string?>(demo, "_status");
        Assert.StartsWith("Read failed:", status);
    }

    [Fact]
    public async Task RemoveAsync_InvokesRemoveItem_ClearsLastRead()
    {
        var js = new FakeJsRuntime();
        var demo = new JsRuntimeDemo(js);
        SetField(demo, "_lastRead", "previous");

        await InvokePrivate(demo, "RemoveAsync");

        Assert.Equal(1, js.CallCount("sessionStorage.removeItem"));
        Assert.Null(GetField<string?>(demo, "_lastRead"));
        Assert.Equal("Removed", GetField<string?>(demo, "_status"));
    }

    [Fact]
    public async Task RemoveAsync_ThrowingJs_SetsStatusRemoveFailed()
    {
        var js = new FakeJsRuntime();
        js.SetException("sessionStorage.removeItem", new InvalidOperationException("nope"));
        var demo = new JsRuntimeDemo(js);

        await InvokePrivate(demo, "RemoveAsync");

        var status = GetField<string?>(demo, "_status");
        Assert.StartsWith("Remove failed:", status);
    }

    [Fact]
    public void RouteAttribute_RegisteredAt_Jsruntime()
    {
        var attr = typeof(JsRuntimePage)
            .GetCustomAttribute<RouteAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("jsruntime", attr!.Template);
    }

    private static async Task InvokeOnRenderedAsync(JsRuntimeDemo demo, bool firstRender)
    {
        var mi = typeof(JsRuntimeDemo).GetMethod("OnRenderedAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)mi.Invoke(demo, [firstRender])!;
    }

    private static async Task InvokePrivate(JsRuntimeDemo demo, string name)
    {
        var mi = typeof(JsRuntimeDemo).GetMethod(name,
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)mi.Invoke(demo, null)!;
    }

    private static T GetField<T>(JsRuntimeDemo demo, string name)
    {
        var f = typeof(JsRuntimeDemo).GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var v = f.GetValue(demo);
        return v is null ? default! : (T)v;
    }

    private static void SetField(JsRuntimeDemo demo, string name, object? value)
    {
        var f = typeof(JsRuntimeDemo).GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        f.SetValue(demo, value);
    }
}
