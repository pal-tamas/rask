using Rask.Core.Live;

namespace Rask.Core.Tests;

#pragma warning disable RASK014 // the tests need the very instance they hand to the render context

// The eager reset's callback block is guarded by a per-component bit (Component.FlagCallbackAssigned)
// so an element that never names a callback skips ~88 delegate writes per render. These pin the thing
// that guard could plausibly break: a callback the chain named LAST render and does not name this one
// must still be gone.
public partial class BuilderCallbackResetTests : global::Rask.Core.RaskMarkup
{
    private static string Render(Component host, IServiceProvider sp)
    {
        using var ctx = LiveRenderContext.Begin(host, sp);
        var resolved = ctx.GetOrCreate(_ => host);
        ctx.NotifyParameters(resolved, propsChanged: true);
        return resolved.ToHtml();
    }

    // The whole point of the eager reset. Render once WITH a handler, then again without: the second
    // render must not still carry it. The guard makes this the case that has to keep working — the bit
    // was set by the first render's setter, so the second render's reset has to act on it.
    [Fact]
    public void A_callback_the_chain_stops_naming_is_gone_next_render()
    {
        var sp = RenderHarness.EmptyServices();
        var host = new DroppingCallbackHost();

        var withHandler = Render(host, sp);
        Assert.Contains("data-rask-on-click", withHandler, StringComparison.Ordinal);

        host.Wired = false;
        var without = Render(host, sp);
        Assert.DoesNotContain("data-rask-on-click", without, StringComparison.Ordinal);
    }

    // And the reverse, so the guard cannot "fix" the above by clearing something it should not: a
    // handler named on every render survives every render.
    [Fact]
    public void A_callback_named_every_render_survives()
    {
        var sp = RenderHarness.EmptyServices();
        var host = new DroppingCallbackHost();

        Assert.Contains("data-rask-on-click", Render(host, sp), StringComparison.Ordinal);
        Assert.Contains("data-rask-on-click", Render(host, sp), StringComparison.Ordinal);
        Assert.Contains("data-rask-on-click", Render(host, sp), StringComparison.Ordinal);
    }

    // An element that never carries a callback is the case the guard exists for — it must render the
    // same markup it always did, having skipped the block entirely.
    [Fact]
    public void An_element_that_never_names_a_callback_renders_unchanged()
    {
        var sp = RenderHarness.EmptyServices();
        var host = new NoCallbackHost();

        var first = Render(host, sp);
        Assert.Equal("<div class=\"x\"><span>hi</span></div>", first);
        Assert.Equal(first, Render(host, sp));
    }

    // Two siblings, one wired and one not, on the same render. The bit is per component instance, so a
    // wired sibling must not drag the unwired one into carrying a handler — nor vice versa.
    [Fact]
    public void A_wired_sibling_does_not_leak_onto_an_unwired_one()
    {
        var sp = RenderHarness.EmptyServices();
        var host = new MixedSiblingHost();

        var html = Render(host, sp);
        Assert.Equal(1, CountOccurrences(html, "data-rask-on-click"));
        Assert.Equal(html, Render(host, sp));
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal);
             i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    internal sealed partial class DroppingCallbackHost : Component
    {
        internal bool Wired = true;

        protected override Component? Render() =>
            Wired ? Div.OnClick(() => { })["x"] : Div["x"];
    }

    internal sealed partial class NoCallbackHost : Component
    {
        protected override Component? Render() => Div.Class("x")[Span["hi"]];
    }

    internal sealed partial class MixedSiblingHost : Component
    {
        protected override Component? Render() =>
            Div[Span.OnClick(() => { })["a"], Span["b"]];
    }
}
