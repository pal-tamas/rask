using System.Text.Json;
using Rask.Core.Components;
using Rask.Core.Live;
using static Rask.Core.Tags;

namespace Rask.Core.Tests.Live;

public class LiveViewTests
{
    [Fact]
    public void RenderAsLiveRoot_RegistersHandlersAndEmitsIds()
    {
        var counter = 0;
        var view = new StubComponent(() => Div(Children:
        [
            Button(OnClick: () => counter++, Children: ["up"]),
            Button(OnClick: () => counter--, Children: ["down"])
        ]));

        var html = view.RenderAsLiveRoot();

        Assert.Equal(
            "<div><button data-rask-on-click=\"h0\">up</button><button data-rask-on-click=\"h1\">down</button></div>",
            html);
    }

    [Fact]
    public void RenderAsLiveRoot_ResetsHandlerIdsBetweenRenders()
    {
        var view = new StubComponent(() => Button(OnClick: () => { }, Children: ["x"]));

        var first = view.RenderAsLiveRoot();
        var second = view.RenderAsLiveRoot();

        Assert.Equal("<button data-rask-on-click=\"h0\">x</button>", first);
        Assert.Equal("<button data-rask-on-click=\"h0\">x</button>", second);
    }

    [Fact]
    public async Task TryInvokeHandlerAsync_RunsParameterlessAction_AndIgnoresPayload()
    {
        var fired = 0;
        var view = new StubComponent(() => Button(OnClick: () => fired++, Children: ["x"]));
        view.RenderAsLiveRoot();

        var ok = await view.TryInvokeHandlerAsync("h0", JsonDocument.Parse("{}").RootElement);

        Assert.True(ok);
        Assert.Equal(1, fired);
    }

    [Fact]
    public async Task TryInvokeHandlerAsync_StringActionHandler_ReceivesValueProperty()
    {
        var captured = string.Empty;
        var view = new StubComponent(() => new Input(new Input.Props(OnInput: v => captured = v)));
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
        var view = new StubComponent(() => Form(OnSubmit: f => captured = f, Children: []));
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
        var view = new StubComponent(() => Button(OnClickAsync: async () =>
        {
            await Task.Yield();
            fired = true;
        }, Children: ["x"]));
        view.RenderAsLiveRoot();

        var ok = await view.TryInvokeHandlerAsync("h0", JsonDocument.Parse("{}").RootElement);

        Assert.True(ok);
        Assert.True(fired);
    }

    [Fact]
    public async Task TryInvokeHandlerAsync_FuncStringTaskHandler_ReceivesValue()
    {
        var captured = string.Empty;
        var view = new StubComponent(() => new Input(new Input.Props(OnInputAsync: async v =>
        {
            await Task.Yield();
            captured = v;
        })));
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
        var view = new StubComponent(() => Form(OnSubmitAsync: async f =>
        {
            await Task.Yield();
            captured = f;
        }, Children: []));
        view.RenderAsLiveRoot();

        using var doc = JsonDocument.Parse("{\"form\":{\"x\":\"y\"}}");
        var ok = await view.TryInvokeHandlerAsync("h0", doc.RootElement);

        Assert.True(ok);
        Assert.Equal("y", captured!["x"]);
    }

    [Fact]
    public async Task TryInvokeHandlerAsync_AsyncSetsAttributeAndPrefersSyncWhenBoth()
    {
        var syncFired = 0;
        var asyncFired = 0;
        var view = new StubComponent(() => Button(
            OnClick: () => syncFired++,
            OnClickAsync: async () =>
            {
                await Task.Yield();
                asyncFired++;
            },
            Children: ["x"]));

        // emits attribute even when both set; sync wins when both registered
        var html = view.RenderAsLiveRoot();
        Assert.Contains("data-rask-on-click=\"h0\"", html);

        var ok = await view.TryInvokeHandlerAsync("h0", JsonDocument.Parse("{}").RootElement);

        Assert.True(ok);
        Assert.Equal(1, syncFired);
        Assert.Equal(0, asyncFired);
    }

    [Fact]
    public async Task TryInvokeHandlerAsync_UnknownId_ReturnsFalse()
    {
        var view = new StubComponent(() => Button(OnClick: () => { }, Children: ["x"]));
        view.RenderAsLiveRoot();

        var ok = await view.TryInvokeHandlerAsync("hX", JsonDocument.Parse("{}").RootElement);

        Assert.False(ok);
    }

    [Fact]
    public async Task TryInvokeHandlerAsync_StaleIdAfterRerender_ReturnsFalse()
    {
        var view = new StubComponent(() => Button(OnClick: () => { }, Children: ["x"]));
        view.RenderAsLiveRoot();
        view.RenderAsLiveRoot();

        var ok = await view.TryInvokeHandlerAsync("h99", JsonDocument.Parse("{}").RootElement);

        Assert.False(ok);
    }
}
