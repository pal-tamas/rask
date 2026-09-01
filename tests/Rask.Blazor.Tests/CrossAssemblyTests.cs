using Microsoft.Extensions.DependencyInjection;
using Rask.Blazor.Library.Fixture;
using Rask.Testing;

namespace Rask.Blazor.Tests;

/// <summary>
///     The headline path: a component from a REFERENCED assembly, compiled from a real
///     <c>.razor</c> by the Razor SDK.
/// </summary>
/// <remarks>
///     Every other fixture here is a hand-written <c>ComponentBase</c> in this compilation, where the
///     symbols come from source. That cannot show what the feature actually promises — reading a
///     hosted component's <c>[Parameter]</c>s out of another assembly's metadata, which is what
///     hosting MudBlazor or your own Razor Class Library really does.
/// </remarks>
public partial class CrossAssemblyTests : global::Rask.Core.RaskMarkup
{
    private static IServiceProvider Services() => new ServiceCollection().BuildServiceProvider();

    [Fact]
    public void An_empty_island_over_a_REFERENCED_razor_component_gets_its_steps()
    {
        var html = RaskTest.Render(TickerIsland.Symbol("RASK").Price(12.5m), Services()).Html;

        Assert.Contains("<strong>RASK</strong>", html, StringComparison.Ordinal);
        Assert.Contains("<span>12.50</span>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void EditorRequired_in_the_referenced_component_is_required_here()
    {
        // Ticker.razor marks Symbol [EditorRequired]; Tone and Note are plain [Parameter].
        var html = RaskTest.Render(TickerIsland.Symbol("RASK").Tone("up"), Services()).Html;

        Assert.Contains("class=\"ticker up\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unset_optional_parameter_leaves_the_components_own_markup_alone()
    {
        // Note is null, so its key is omitted and the component's @if never renders the <em>.
        var html = RaskTest.Render(TickerIsland.Symbol("RASK"), Services()).Html;

        Assert.DoesNotContain("<em>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_bound_input_is_wired_to_Rasks_VALUE_channel_not_its_DOM_event_channel()
    {
        // @bind lowers to `value=` plus an onchange handler reading ChangeEventArgs.Value, so the
        // browser's value has to reach the hosted component. Rask carries a value only on its input
        // channel — data-rask-on-input, which ships {id, type:"input", value} — so binding works only
        // if the handler lands THERE rather than on data-rask-on-change as a bare DOM event.
        var html = RaskTest.Render(EditorIsland.Text("hello"), Services()).Html;

        Assert.Contains("data-rask-on-input=", html, StringComparison.Ordinal);

        // The marker that makes the dispatch synchronous — `change` means the value is final rather
        // than still being typed. The client still reads the id from data-rask-on-input.
        Assert.Contains("data-rask-on-change=", html, StringComparison.Ordinal);

        // And the current value is rendered, so the input is not blank on first paint.
        Assert.Contains("value=\"hello\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Typing_into_a_bound_input_reaches_the_hosted_component_and_re_renders()
    {
        // The round trip, not just the wiring: the browser's value travels Rask's input channel,
        // becomes a ChangeEventArgs, is dispatched into Blazor, assigns the bound field through the
        // binder @bind generated, and the component's own re-render reaches the page.
        var page = RaskTest.Render(EditorIsland.Text("hello"), Services());
        Assert.Contains("echo: hello", page.Html, StringComparison.Ordinal);

        await page.InputAsync("{\"value\":\"typed\"}");

        Assert.Contains("echo: typed", page.Html, StringComparison.Ordinal);
        Assert.Contains("value=\"typed\"", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clicking_the_hosted_components_own_element_reaches_its_EventCallback()
    {
        var picked = "";
        var page = RaskTest.Render(
            TickerIsland.Symbol("RASK").OnPick(s => picked = s),
            Services());

        await page.On("[data-rask-on-click]").ClickAsync();

        Assert.Equal("RASK", picked);
    }

    [Fact]
    public void Rask_children_render_inside_the_referenced_component()
    {
        var html = RaskTest
            .Render(TickerIsland.Symbol("RASK")[Button.OnClick(() => { })["Buy"]], Services())
            .Html;

        Assert.Contains("Buy", html, StringComparison.Ordinal);
        // The child's handler is Rask's own, delegated from document — it survives the island.
        Assert.Contains("data-rask-on-click=", html, StringComparison.Ordinal);
    }
}

/// <summary>An island over a component this assembly does not declare. Body deliberately empty.</summary>
public sealed partial class TickerIsland : BlazorComponent<Ticker>;

/// <summary>An island over a component that uses real two-way <c>@bind</c>.</summary>
public sealed partial class EditorIsland : BlazorComponent<Editor>;
