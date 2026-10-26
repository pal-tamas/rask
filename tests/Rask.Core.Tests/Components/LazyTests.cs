using System.Text.Json;

#pragma warning disable RASK014 // StubComponent has no generated factory

namespace Rask.Core.Tests.Components;

// Lazy.Placeholder(…)[Widget]: the widget is not mounted until the browser says the placeholder is in view
// (rask-lazy.ts raises `load` on it), and then exactly once. Livewire's lazy component.
public sealed partial class LazyTests : global::Rask.Core.RaskMarkup
{
    // System.Lazy<T> beside the Lazy entry, in a file with `using System;` (an implicit using here): the two
    // never meet, because a name with a type argument only ever finds a generic type.
    private readonly Lazy<string> _greeting = new(() => "hello");

    [Fact]
    public void The_placeholder_is_drawn_and_the_wrapped_component_is_not_mounted_before_it_is_in_view()
    {
        var mounts = new LazyMounts();
        var view = new StubComponent(() => Lazy.Placeholder(Span["loading"])[LazyProbeWidget.Mounts(mounts)]);

        var html = view.RenderAsLiveRoot();

        Assert.Contains("data-rask-lazy", html, StringComparison.Ordinal);
        Assert.Contains("<span>loading</span>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("sales", html, StringComparison.Ordinal);
        Assert.Equal(0, mounts.Count);
    }

    [Fact]
    public async Task The_wrapped_component_mounts_once_when_the_placeholder_comes_into_view()
    {
        var mounts = new LazyMounts();
        var view = new StubComponent(() => Lazy.Placeholder(Span["loading"])[LazyProbeWidget.Mounts(mounts)]);
        var inView = MarkupAssert.Attr(view.RenderAsLiveRoot(), "data-rask-on-load")!;

        await Load(view, inView);
        var html = view.RenderAsLiveRoot();
        await Load(view, inView);
        var again = view.RenderAsLiveRoot();

        Assert.Equal(1, mounts.Count);
        Assert.Contains("<p>sales</p>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-rask-lazy", html, StringComparison.Ordinal);
        Assert.DoesNotContain("loading", html, StringComparison.Ordinal);
        Assert.Equal(html, again);
    }

    [Fact]
    public void Without_a_placeholder_an_empty_div_stands_in_as_livewires_does()
    {
        var view = new StubComponent(() => Lazy[LazyProbeWidget.Mounts(new LazyMounts())]);

        var html = view.RenderAsLiveRoot();

        Assert.Equal("<div data-rask-lazy=\"\" data-rask-on-load=\"h0\"></div>", html);
    }

    [Fact]
    public void System_lazy_and_the_lazy_entry_are_both_reachable_in_one_component()
    {
        var view = new StubComponent(() => Lazy.Placeholder(Span[_greeting.Value])[Span["later"]]);

        var html = view.RenderAsLiveRoot();

        Assert.Contains("<span>hello</span>", html, StringComparison.Ordinal);
    }

    private static async Task Load(StubComponent view, string handler)
    {
        using var payload = JsonDocument.Parse("""{"type":"load","unasked":true}""");
        await view.TryInvokeHandlerAsync(handler, payload.RootElement);
    }
}

/// <summary>How many times a <see cref="LazyProbeWidget" /> mounted, one per test.</summary>
public sealed class LazyMounts
{
    public int Count { get; set; }
}

/// <summary>Counts its mounts on the <see cref="LazyMounts" /> it is given.</summary>
public sealed partial class LazyProbeWidget : Component
{
    /// <summary>Where the mounts are counted.</summary>
    public LazyMounts? Mounts { get; set; }

    protected override Task OnMount()
    {
        if (Mounts is { } mounts)
        {
            mounts.Count++;
        }

        return Task.CompletedTask;
    }

    protected override Component? Render() => P["sales"];
}
