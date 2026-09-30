using Rask.Site.Tests.Infrastructure;
using Rask.Web;

namespace Rask.Site.Tests.Demos;

/// <summary>
///     The Web APIs demo, tested the way an app tests its own components: the web object it calls is faked, so the test
///     runs with no browser and says what the component asked for.
/// </summary>
public sealed partial class WebApiDemoTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public async Task Round_tripping_local_storage_shows_what_the_browser_holds()
    {
        using var storage = LocalStorage.Fake();
        storage.Returns(s => s.GetItem("rask-web-demo"), "held by the fake");
        var page = Page.Render(() => WebApiDemo, TestServices.Default());

        await page.InvokeAsync(HandlerIn(page.Render(), "id=\"web-store\"", "data-rask-on-click"), "{}");

        Assert.Contains("localStorage says: held by the fake", page.Render(), StringComparison.Ordinal);
        Assert.Equal(["setItem", "getItem"], storage.Calls.Select(c => c.Member));
    }

    private static string HandlerIn(string html, string anchor, string attr)
    {
        var marker = attr + "=\"";
        var tag = html.Split('<').First(t => t.Contains(anchor, StringComparison.Ordinal) && t.Contains(marker, StringComparison.Ordinal));
        var start = tag.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        return tag[start..tag.IndexOf('"', start)];
    }
}
