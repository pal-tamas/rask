using Rask.Core.Live;
using Rask.Site;
using Rask.Site.Features;
using Rask.Site.Tests.Infrastructure;

namespace Rask.Site.Tests.Demos;

// The data demo is a separate app (src/Rask.Site.DataDemo) that pages.yml publishes to /demos/data/; a guide embeds it
// through the `data-notes` key as a lazy frame. What must hold here is the part only this side can get wrong: the frame
// points at that path under the site's own path base, is named for a screen reader, and loads only when scrolled to —
// an eager frame would download EF Core and SQLite on every visit to the guide.
public sealed class DataNotesDemoTests
{
    [Fact]
    public void The_data_notes_key_is_registered() => Assert.True(DemoRegistry.Contains("data-notes"));

    [Fact]
    public void It_frames_the_demo_app_lazily_under_the_path_base_with_a_title()
    {
        var html = Render();
        var src = LiveOptions.PathBase + "/demos/data/";

        Assert.Contains("<iframe", html, StringComparison.Ordinal);
        Assert.Contains($"title=\"{DataNotesDemo.FrameTitle}\"", html, StringComparison.Ordinal);
        Assert.Contains($"src=\"{src}\"", html, StringComparison.Ordinal);
        Assert.Contains("loading=\"lazy\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void It_offers_the_demo_full_screen_in_the_same_tab()
    {
        var html = Render();

        // Same tab on purpose: a second tab beside the frame would not own the demo's database.
        var link = html[html.IndexOf("<a ", StringComparison.Ordinal)..];
        Assert.Contains($"href=\"{LiveOptions.PathBase}/demos/data/\"", link[..link.IndexOf('>')], StringComparison.Ordinal);
        Assert.Contains(">Open full screen</a>", link, StringComparison.Ordinal);
        Assert.DoesNotContain("target=\"_blank\"", html, StringComparison.Ordinal);
    }

    private static string Render() =>
        new LiveHost(() => DemoRegistry.Build("data-notes"), TestServices.Default()).RenderAsLiveRoot();
}
