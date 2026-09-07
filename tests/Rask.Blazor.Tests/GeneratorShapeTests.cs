using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Rask.Testing;

namespace Rask.Blazor.Tests;

/// <summary>
///     Shapes the Blazor island generator has to handle in the DECLARATION, rather than in the hosted
///     component.
/// </summary>
/// <remarks>
///     A GENERIC container is deliberately not covered here. The Blazor generator now handles it — it
///     re-opens `Box&lt;T&gt;` with its type parameters and no longer feeds '&lt;' to a hint name — but
///     `ComponentFactoryGenerator` then emits a chain entry naming `Box&lt;T&gt;.Boxed` with no `T` in
///     scope, so the compilation fails in a different generator's output. That is its own defect and
///     filed as one; a fixture for it here would be red for a reason this file is not about.
///
///     Every one of these fails at COMPILE time when the generator gets it wrong, which is the point:
///     a missing generated part means the chain step does not exist, and the call site fails as CS1929
///     against an unrelated overload rather than as anything naming the island. So the assertions below
///     are almost incidental — that this file builds at all is most of the test.
/// </remarks>
public partial class GeneratorShapeTests : global::Rask.Core.RaskMarkup
{
    private static IServiceProvider Services() => new ServiceCollection().BuildServiceProvider();

    [Fact]
    public void An_island_nested_two_levels_deep_is_generated_for()
    {
        // Types() walked one level of nesting by hand, so a container inside a container was never
        // reached (#949). Containers() already walked to arbitrary depth, so the intent was never
        // one level — the two halves simply disagreed, silently.
        var html = RaskTest.Render(DeepIsland.Marker("deep"), Services()).Html;

        Assert.Contains("deep", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_hosted_parameter_named_Key_still_gets_a_property()
    {
        // Collecting "members the author declared" walked the BASE types too, so every instance member
        // Rask puts on Component looked hand-written. A hosted [Parameter] named Key matched
        // Component.Key and was dropped: no property, no chain step, no diagnostic — and the island then
        // fed Rask's reconciliation key to the hosted component's own parameter (#950).
        //
        // Asserted on the generated TYPE rather than on rendered HTML, because that is exactly what the
        // bug removed. `Key` also exists on Component, so the one declared HERE is the generated one.
        var key = typeof(KeyedIsland).GetProperty(
            "Key",
            System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.DeclaredOnly);

        Assert.NotNull(key);
        Assert.Equal(typeof(string), key!.PropertyType);
    }

    [Fact]
    public void A_hosted_parameter_that_shadows_nothing_reaches_the_component()
    {
        var html = RaskTest.Render(KeyedIsland.Caption("shown"), Services()).Html;

        Assert.Contains("shown", html, StringComparison.Ordinal);
    }
}


/// <summary>A hosted component whose parameter collides with <c>Component.Key</c>.</summary>
public sealed class KeyedBox : ComponentBase
{
    [Parameter] public string? Caption { get; set; }

    /// <summary>Collides by name with <c>Rask.Core.Component.Key</c>, which is the whole point.</summary>
    [Parameter] public string? Key { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "p");
        builder.AddContent(1, Caption ?? "(none)");
        builder.CloseElement();
    }
}

/// <summary>A hosted component with a single label, for the nesting fixtures.</summary>
public sealed class LabelBox : ComponentBase
{
    [Parameter] public string? Marker { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "span");
        builder.AddContent(1, Marker ?? "(none)");
        builder.CloseElement();
    }
}

/// <summary>Two levels of containment, which the one-level walk never reached.</summary>
public static partial class Outer
{
    public static partial class Middle
    {
        public sealed partial class DeepIsland : BlazorComponent<LabelBox>;
    }

}

/// <summary>An island whose hosted component declares a parameter named <c>Caption</c>.</summary>
public sealed partial class KeyedIsland : BlazorComponent<KeyedBox>;
