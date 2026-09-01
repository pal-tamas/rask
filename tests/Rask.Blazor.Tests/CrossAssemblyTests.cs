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
