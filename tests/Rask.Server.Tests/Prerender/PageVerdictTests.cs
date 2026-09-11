using Rask.Core;
using Rask.Core.Rendering;
using Rask.Server.Prerender;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Server.Tests.Prerender;

// The decisions a page render ends in, pinned as truth tables. They were inline in the GET handler, and
// they are shared now by everything that renders a page — so the rules have to hold on their own, not
// merely through whichever request happened to exercise them.
public class PageVerdictTests
{
    [Theory]
    // interactivity, staticPages, declaredStatic, requiresLive, faulted, jsPending, development → needs a session
    [InlineData(true, false, false, false, false, false, false, true)] // the default: every page is live
    [InlineData(true, true, false, false, false, false, false, false)] // static pages on, nothing needs a socket
    [InlineData(true, false, true, false, false, false, false, false)] // declared static on an interactive app
    [InlineData(true, true, false, true, false, false, false, true)] // a handler keeps it live
    [InlineData(true, true, false, false, true, false, false, true)] // a crashed page keeps "Try again" working
    [InlineData(true, true, false, false, false, true, false, true)] // a queued JS call needs a frame to ride
    [InlineData(true, true, false, false, false, false, true, true)] // Development keeps every page live
    [InlineData(false, true, false, true, true, true, true, false)] // interactivity off outranks every reason
    public void NeedsSession_FollowsTheRules(
        bool serverInteractivity,
        bool staticPages,
        bool declaredStatic,
        bool requiresLiveSession,
        bool faulted,
        bool jsPending,
        bool development,
        bool expected)
    {
        Assert.Equal(
            expected,
            PageVerdict.NeedsSession(
                serverInteractivity, staticPages, declaredStatic, requiresLiveSession, faulted, jsPending, development));
    }

    [Theory]
    [InlineData(false, null, false, 200)]
    [InlineData(false, null, true, 404)] // the not-found page, actually mounted
    [InlineData(false, 200, true, 200)] // a deliberate soft 404
    [InlineData(false, 410, false, 410)] // a page's own status
    [InlineData(true, 200, true, 500)] // a page that threw does not get to claim it succeeded
    public void Status_OrdersFaultOverThePageOverTheRouter(
        bool faulted, int? declaredStatus, bool notFoundMounted, int expected)
    {
        Assert.Equal(expected, PageVerdict.Status(faulted, declaredStatus, notFoundMounted));
    }

    [Fact]
    public void DeclaredStatic_IsReadFromTheRoutedPageAlone()
    {
        Assert.True(PageVerdict.DeclaredStatic([typeof(VerdictLayout), typeof(VerdictStaticPage)], null));

        // Declared on a layout, not the page: a helper up the chain does not get to force a page static.
        Assert.False(PageVerdict.DeclaredStatic([typeof(VerdictStaticPage), typeof(VerdictLayout)], null));

        // The not-found page's route is incidental, so its declaration is too.
        Assert.False(PageVerdict.DeclaredStatic([typeof(VerdictStaticPage)], typeof(VerdictStaticPage)));

        Assert.False(PageVerdict.DeclaredStatic([], null));
    }
}

[RenderMode(RenderMode.Static)]
public sealed partial class VerdictStaticPage : Component
{
    protected override Component? Render() => P["declared static"];
}

public sealed partial class VerdictLayout : Component
{
    protected override Component? Render() => Div["layout"];
}
