using Rask.Core.Components;
using Rask.Core.Routing;
using Rask.Html.Components;
using Rask.Ui;

namespace Rask.Example.Shared.Features;

// The site root: the repo's user guides (docs/*.md) rendered on-site, grouped as cards. Guides-first, so
// this is served at "/" (the old Welcome landing page is gone). Each card links to /guides/{slug}, where
// GuidePage renders the markdown with Markdig.
[Route("")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class GuidesIndexPage : Component
{
    protected override Component? HeadAssets => Title["Guides — Rask"];

    protected override Component? Render() =>
    [
        PageHeader
            .Title("Guides")
            .Lead("Narrative documentation for the framework — the same guides that ship in the repo's docs/ "
                  + "folder, rendered here. Each guide embeds runnable demos inline and reads like a proper "
                  + "narrative guide, with a Chapters index, an on-this-page rail, and prev/next navigation."),
        Install(),
        Div[GuideCards]
    ];

    /// <summary>
    /// How to get a Rask project, above the guides rather than buried in one of them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This page is what <c>rask.sh/docs/</c> serves, and it opened straight into guide cards — a visitor
    /// who landed here was never told how to install anything. The landing site had the commands and the
    /// documentation site did not.
    /// </para>
    /// <para>
    /// The strings are repeated here rather than shared with the landing site's <c>InstallTabs</c>: that
    /// app deliberately references none of this project (it is the whole showcase, and the site is a
    /// size-tuned marketing bundle). Repeating them is safe only because every copy is pinned —
    /// <c>scripts/tests/install-script.test.sh</c> asserts this file alongside the README, NUGET.md,
    /// llms.txt, three docs pages and the site's own copy.
    /// </para>
    /// </remarks>
    private static Component Install() =>
        Div.Class("install-block mb-6 rounded-xl border border-ui-line bg-ui-panel p-4 sm:p-5")[
            Div.Class("flex items-center gap-2")[
                UiIcon.Name(UiIconName.Terminal).Class("size-5 shrink-0 text-ui-brand"),
                H2.Class("text-base font-semibold tracking-tight text-ui-ink sm:text-lg")["Start a project"]
            ],
            P.Class("mt-1 text-sm text-ui-muted")[
                "Nothing preinstalled — the installer brings the .NET 10 SDK with it, under ",
                Code["$HOME"], ", with no ", Code["sudo"], "."
            ],
            Pre.Class(
                "mt-3 overflow-x-auto rounded-lg border border-ui-line bg-ui-well p-4 font-mono text-xs "
                + "leading-relaxed text-ui-ink")[
                Code[
                    Prompt(), " curl -sSL https://rask.sh/rask.sh | sh\n",
                    Prompt(), " rask new MyApp\n",
                    Prompt(), " cd MyApp && rask dev"
                ]
            ],
            P.Class("mt-3 text-xs text-ui-muted")[
                "Windows: ", Code["irm https://rask.sh/rask.ps1 | iex"], ". Add ",
                Code["--template wasm"], " for a standalone browser app, or ", Code["--auth"],
                " for a cookie/JWT starter."
            ]
        ];

    private static Component Prompt() => Span.Class("select-none text-ui-brand")["$"];
}
