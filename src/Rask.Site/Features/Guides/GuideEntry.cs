using Rask;

namespace Rask.Site.Features;

// The complete set of repo guides surfaced on-site, in display order and grouped. Each entry's Slug is a
// docs file's bare leaf name (docs/routing.md and docs/apis/geolocation.md -> "routing"/"geolocation"),
// which is exactly what Markdown.RewriteLinks routes an in-doc "*.md" link to (/guides/{leaf}) and what
// the /guides/{slug} route binds. Leaf names are unique across docs/ (guarded by the build). For a guide
// that lives in a subfolder, Source carries the real docs-relative path for the "edit on GitHub" link,
// while top-level guides leave it null (defaulting to "{Slug}.md").
//
// Shared by GuidesIndexPage (the cards) and the sidebar's Guides section so the two never drift.
// GuidesTests guards both directions: every slug resolves to an embedded doc, and every embedded
// user-facing doc appears here — so a doc can never be added to the repo yet hidden from the site.
public sealed record GuideEntry(string Slug, string Title, string Blurb, string Group, string? Source = null)
{
    /// <summary>The guide's <c>&lt;title&gt;</c> in a search result, without the site suffix.</summary>
    /// <remarks>
    ///     Separate from <see cref="Title" />, which the sidebar and the prev/next links show and so has to be
    ///     short. A search result is read by someone who has never seen the sidebar: "CQRS" says nothing
    ///     there, "CQRS in .NET with source-generated handlers" says what the page is and matches what they
    ///     typed. <c>required</c>, so a guide added without one does not compile; the length and uniqueness
    ///     rules are asserted by <c>GuideSearchCopyTests</c>.
    /// </remarks>
    public required string SearchTitle { get; init; }

    /// <summary>The guide's <c>&lt;meta name="description"&gt;</c>: 110–160 characters about THIS page.</summary>
    /// <remarks>
    ///     Longer than <see cref="Blurb" />, which is the one-line card text. A description is what a search
    ///     result shows under the title, and the blurb it used to be — "Typed browser API: IBattery." on
    ///     fifty-two pages — told a reader nothing and matched nothing anyone would type.
    /// </remarks>
    public required string Description { get; init; }

    /// <summary>The group's icon.</summary>
    /// <remarks>
    /// Derived rather than stored. Every guide used to name its own, which meant 67 distinct glyphs
    /// across ~80 guides — and the sidebar already groups them, so the icon only ever repeated what the
    /// heading said. One per group is the information that was actually there, and a guide added later
    /// cannot forget to pick one.
    /// </remarks>
    public Ui.IconName Icon => Group switch
    {
        "Start here" => Ui.IconName.Rocket,
        "Tutorial" => Ui.IconName.Book,
        "Data" => Ui.IconName.Database,
        "Auth" => Ui.IconName.Lock,
        "Backend services" => Ui.IconName.Server,
        "Realtime" => Ui.IconName.Signal,
        "Frontend" => Ui.IconName.Cube,
        "Deploy & operate" => Ui.IconName.Globe,
        "Browser & devices" => Ui.IconName.Phone,
        "Browser API reference" => Ui.IconName.CodeBracket,
        "Advanced" => Ui.IconName.Sparkles,
        "Contributing & internals" => Ui.IconName.Terminal,
        _ => Ui.IconName.Document,
    };
}
