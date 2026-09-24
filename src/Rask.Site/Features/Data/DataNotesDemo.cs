using Rask.Core.Live;

namespace Rask.Site.Features;

// The full-stack demo, embedded. It is a separate app (src/Rask.Site.DataDemo) published beside the site at
// /demos/data/, because EF Core, Rask.Data and a native SQLite would add about 2.4 MB brotli to every visit of the
// site itself; framed here, it is downloaded only when a reader scrolls a guide to it (loading="lazy").
//
// "Open full screen" navigates this tab rather than opening another: the demo's database belongs to one tab at a
// time, and a second tab beside this frame would open an empty one.
public sealed partial class DataNotesDemo : Component
{
    /// <summary>Where pages.yml puts the demo app, under the site's own path base.</summary>
    public const string Path = "/demos/data/";

    /// <summary>What the frame is announced as — a frame's title is its accessible name.</summary>
    public const string FrameTitle = "Notes demo: a SQLite database in your browser, written and searched with Rask.Data";

    private static string Src => LiveOptions.PathBase + Path;

    protected override Component? Render() =>
        Div.Class("flex flex-col gap-2")[
            Iframe.Src(Src).Title(FrameTitle).Loading("lazy").Height(640)
                .Class("block w-full rounded-xl border border-base-300 bg-base-100"),
            A.Href(Src).Class("self-start text-sm font-medium underline underline-offset-4")["Open full screen"]
        ];
}
