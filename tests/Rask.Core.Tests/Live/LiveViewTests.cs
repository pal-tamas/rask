using System.Text.Json;
using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Live;

public partial class LiveViewTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Rendering_as_the_live_root_registers_handlers_and_emits_ids()
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
    public void Rendering_as_the_live_root_resets_handler_ids_between_renders()
    {
        var view = new StubComponent(() => Button.OnClick(() => { })["x"]);

        var first = view.RenderAsLiveRoot();
        var second = view.RenderAsLiveRoot();

        Assert.Equal("<button data-rask-on-click=\"h0\">x</button>", first);
        Assert.Equal("<button data-rask-on-click=\"h0\">x</button>", second);
    }

    [Fact]
    public async Task Invoking_a_parameterless_action_handler_runs_it_and_ignores_the_payload()
    {
        var fired = 0;
        var view = new StubComponent(() => Button.OnClick(() => fired++)["x"]);
        view.RenderAsLiveRoot();

        var ok = await view.TryInvokeHandlerAsync("h0", JsonDocument.Parse("{}").RootElement);

        Assert.True(ok);
        Assert.Equal(1, fired);
    }

    [Fact]
    public async Task A_string_action_handler_receives_the_value_property()
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
    public async Task A_FormData_action_handler_receives_the_form_fields()
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
    public async Task A_task_returning_handler_is_awaited()
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
    public async Task A_task_returning_string_handler_receives_the_value()
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
    public async Task A_task_returning_FormData_handler_receives_the_form()
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
    public async Task The_last_write_wins_the_handler_slot()
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
    public async Task Invoking_an_unknown_handler_id_reports_false()
    {
        var view = new StubComponent(() => Button.OnClick(() => { })["x"]);
        view.RenderAsLiveRoot();

        var ok = await view.TryInvokeHandlerAsync("hX", JsonDocument.Parse("{}").RootElement);

        Assert.False(ok);
    }

    [Fact]
    public async Task Invoking_a_stale_handler_id_after_a_rerender_reports_false()
    {
        var view = new StubComponent(() => Button.OnClick(() => { })["x"]);
        view.RenderAsLiveRoot();
        view.RenderAsLiveRoot();

        var ok = await view.TryInvokeHandlerAsync("h99", JsonDocument.Parse("{}").RootElement);

        Assert.False(ok);
    }
}
