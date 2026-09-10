using System.Text.Json;
using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Live;

public partial class LiveViewTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void RenderAsLiveRoot_RegistersHandlersAndEmitsIds()
    {
        var counter = 0;
        var view = new StubComponent(() => Div[
            Button.OnClick(() => counter++)["up"],
            Button.OnClick(() => counter--)["down"]
        ]);

        var html = view.RenderAsLiveRoot();

        Assert.Equal(
            "<div><button data-rask-on-click=\"h0\">up</button><button data-rask-on-click=\"h1\">down</button></div>",
            html);
    }

    [Fact]
    public void RenderAsLiveRoot_ResetsHandlerIdsBetweenRenders()
    {
        var view = new StubComponent(() => Button.OnClick(() => { })["x"]);

        var first = view.RenderAsLiveRoot();
        var second = view.RenderAsLiveRoot();

        Assert.Equal("<button data-rask-on-click=\"h0\">x</button>", first);
        Assert.Equal("<button data-rask-on-click=\"h0\">x</button>", second);
    }

    [Fact]
    public async Task TryInvokeHandlerAsync_RunsParameterlessAction_AndIgnoresPayload()
    {
        var fired = 0;
        var view = new StubComponent(() => Button.OnClick(() => fired++)["x"]);
        view.RenderAsLiveRoot();

        var ok = await view.TryInvokeHandlerAsync("h0", JsonDocument.Parse("{}").RootElement);

        Assert.True(ok);
        Assert.Equal(1, fired);
    }

    [Fact]
    public async Task TryInvokeHandlerAsync_StringActionHandler_ReceivesValueProperty()
    {
        var captured = string.Empty;
        var view = new StubComponent(() => Input.Value<string>(null).OnInput(v => captured = v));
        view.RenderAsLiveRoot();

        using var doc = JsonDocument.Parse("{\"id\":\"h0\",\"type\":\"input\",\"value\":\"hello\"}");
        var ok = await view.TryInvokeHandlerAsync("h0", doc.RootElement);

        Assert.True(ok);
        Assert.Equal("hello", captured);
    }

    [Fact]
    public async Task TryInvokeHandlerAsync_FormDataActionHandler_ReceivesFormFields()
    {
        FormData? captured = null;
        var view = new StubComponent(() => Form.Model(new object()).OnSubmit(f => captured = f));
        view.RenderAsLiveRoot();

        using var doc =
            JsonDocument.Parse("{\"id\":\"h0\",\"type\":\"submit\",\"form\":{\"name\":\"Bob\",\"age\":\"42\"}}");
        var ok = await view.TryInvokeHandlerAsync("h0", doc.RootElement);

        Assert.True(ok);
        Assert.NotNull(captured);
        Assert.Equal("Bob", captured!["name"]);
        Assert.Equal("42", captured["age"]);
    }

    [Fact]
    public async Task TryInvokeHandlerAsync_FuncTaskHandler_IsAwaited()
    {
        var fired = false;
        var view = new StubComponent(() => Button
            .OnClick(async () =>
        {
            await Task.Yield();
            fired = true;
        })["x"]);
        view.RenderAsLiveRoot();

        var ok = await view.TryInvokeHandlerAsync("h0", JsonDocument.Parse("{}").RootElement);

        Assert.True(ok);
        Assert.True(fired);
    }

    [Fact]
    public async Task TryInvokeHandlerAsync_FuncStringTaskHandler_ReceivesValue()
    {
        var captured = string.Empty;
        var view = new StubComponent(() => Input.Value<string>(null)
            .OnInput(async v =>
        {
            await Task.Yield();
            captured = v;
        }));
        view.RenderAsLiveRoot();

        using var doc = JsonDocument.Parse("{\"value\":\"hi\"}");
        var ok = await view.TryInvokeHandlerAsync("h0", doc.RootElement);

        Assert.True(ok);
        Assert.Equal("hi", captured);
    }

    [Fact]
    public async Task TryInvokeHandlerAsync_FuncFormDataTaskHandler_ReceivesForm()
    {
        FormData? captured = null;
        var view = new StubComponent(() => Form.Model(new object()).OnSubmit(async f =>
        {
            await Task.Yield();
            captured = f;
        }));
        view.RenderAsLiveRoot();

        using var doc = JsonDocument.Parse("{\"form\":{\"x\":\"y\"}}");
        var ok = await view.TryInvokeHandlerAsync("h0", doc.RootElement);

        Assert.True(ok);
        Assert.Equal("y", captured!["x"]);
    }

    [Fact]
    public async Task TryInvokeHandlerAsync_LastWriteWinsTheSlot()
    {
        var syncFired = 0;
        var asyncFired = 0;
        // Writes the step twice, which is now a duplicated step rather than an ambiguous pair — the
        // second write simply takes the slot. RASK044 reports it for real code; here it is the
        // behaviour under test.
#pragma warning disable RASK044
        var view = new StubComponent(() => Button
            .OnClick(() => syncFired++)
            .OnClick(async () =>
            {
                await Task.Yield();
                asyncFired++;
            })["x"]);
#pragma warning restore RASK044

        // The attribute is emitted for whichever handler holds the slot, and dispatch runs that one.
        var html = view.RenderAsLiveRoot();
        Assert.Contains("data-rask-on-click=\"h0\"", html);

        var ok = await view.TryInvokeHandlerAsync("h0", JsonDocument.Parse("{}").RootElement);

        Assert.True(ok);
        Assert.Equal(0, syncFired);
        Assert.Equal(1, asyncFired);
    }

    [Fact]
    public async Task TryInvokeHandlerAsync_UnknownId_ReturnsFalse()
    {
        var view = new StubComponent(() => Button.OnClick(() => { })["x"]);
        view.RenderAsLiveRoot();

        var ok = await view.TryInvokeHandlerAsync("hX", JsonDocument.Parse("{}").RootElement);

        Assert.False(ok);
    }

    [Fact]
    public async Task TryInvokeHandlerAsync_StaleIdAfterRerender_ReturnsFalse()
    {
        var view = new StubComponent(() => Button.OnClick(() => { })["x"]);
        view.RenderAsLiveRoot();
        view.RenderAsLiveRoot();

        var ok = await view.TryInvokeHandlerAsync("h99", JsonDocument.Parse("{}").RootElement);

        Assert.False(ok);
    }
}
