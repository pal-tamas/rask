using System.Text;
using System.Text.RegularExpressions;
using Rask.Core.Live;
using Rask.TestSupport;

namespace Rask.External.Tests;

#pragma warning disable RASK014 // the test hands the very instance it renders to the context

/// <summary>Two external components under one parent, to check they do not renumber each other.</summary>
internal sealed partial class TwoTickerPage : Component
{
    protected override Component? Render() =>
        Div[
            Ticker.OnTick(() => { }),
            Ticker.OnTick(() => { })
        ];
}

/// <summary>A page with an ordinary Rask handler beside an external component's callback.</summary>
internal sealed partial class MixedHandlerPage : Component
{
    protected override Component? Render() =>
        Div[
            Button.OnClick(() => { })["server"],
            Ticker.OnTick(() => { })
        ];
}

// Handler ids are the address a dispatched event resolves through. Two handlers sharing one id is
// not a cosmetic problem: the map is keyed by id, so the second registration overwrites the first and
// a click runs the wrong delegate — or nothing.
//
// This existed because it happened. An external component's callback registered against the COMPONENT
// rather than the render root, which restarted the id sequence at zero, so a page with a Rask button
// beside a component served:
//
//     <button data-rask-on-click="h0">          and          "onBump": { "$h": "h0" }
//
// The same id for two different handlers, and the callback's entry in a map the dispatcher never
// reads. Neither the render tests nor the client node fixture could see it: one asserts on one
// component's markup, the other asserts that calling the function reaches the host channel. It took
// serving a real page to notice, which is the argument for having done that.
public class HandlerIdentityTests
{
    [Fact]
    public void A_callback_does_not_take_an_id_the_page_already_used()
    {
        var html = RenderLive(new MixedHandlerPage());

        var domId = Regex.Match(html, @"data-rask-on-click=""([^""]+)""").Groups[1].Value;
        var callbackId = Regex.Match(html, @"\$h&quot;:&quot;([^&]+)&quot;").Groups[1].Value;

        Assert.False(domId.Length == 0, $"no DOM handler id in:\n{html}");
        Assert.False(callbackId.Length == 0, $"no callback handler id in:\n{html}");

        Assert.True(
            domId != callbackId,
            $"the button and the component's callback share the id '{domId}', so the handler map holds "
            + $"only one of them and a click runs the wrong delegate.\n{html}");
    }

    [Fact]
    public void Two_components_under_one_parent_do_not_share_an_id()
    {
        var html = RenderLive(new TwoTickerPage());

        var ids = Regex.Matches(html, @"\$h&quot;:&quot;([^&]+)&quot;")
            .Select(m => m.Groups[1].Value)
            .ToArray();

        Assert.Equal(2, ids.Length);
        Assert.True(ids[0] != ids[1], $"both callbacks registered as '{ids[0]}':\n{html}");
    }

    /// <summary>Renders through a live context, which is what mints ids at all.</summary>
    private static string RenderLive(Component page)
    {
        using var scope = RenderHarness.Render(page, RenderHarness.EmptyServices());
        return scope.Resolved.ToHtml();
    }
}
