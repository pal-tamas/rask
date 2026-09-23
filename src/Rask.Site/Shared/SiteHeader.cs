using Rask;
using Rask.Core;
using Rask.Core.Routing;

namespace Rask.Site;

/// <summary>
///     The site's one top bar — the landing page's, now worn by <c>/docs</c> as well.
/// </summary>
/// <remarks>
///     <para>
///         There were two. The landing page had this: a <c>&lt;header&gt;</c>, a bolt and a wordmark, and
///         three quiet text links at the trailing edge. <c>/docs</c> had daisyUI's <c>navbar</c>, a
///         hamburger, the brand mark, a "showcase" pill, a version badge, a live <c>path:</c> readout and
///         a bordered <c>★ GitHub</c> button. They already shared a height (<c>--nav-h</c>, 4rem) and a
///         glass background, which is what made the rest of the difference read as drift rather than as
///         design.
///     </para>
///     <para>
///         One component, two call sites. The only thing the docs add is <see cref="Leading" /> — the
///         sidebar's hamburger, which has to be in the bar because that is where a thumb reaches for it.
///     </para>
///     <para>
///         The theme picker is the kit's <see cref="UiThemeDropdown" />, a popover so it closes on Escape
///         and on a click outside; its panel reports its toggle through one C# handler. The hamburger is a
///         label for the sidebar's checkbox. <c>App.ThemeInitJs</c> remembers the theme — the radios are
///         CSS-only, so no handler runs when one is picked.
///     </para>
/// </remarks>
internal sealed partial class SiteHeader : Component
{
    /// <summary>
    ///     Optional content at the leading edge, before the wordmark: the docs sidebar's hamburger.
    ///     Nothing on the landing page, which has no sidebar to toggle.
    /// </summary>
    public Component? Leading { get; set; }

    /// <summary>
    ///     Whether the bar runs the full width of the viewport (the docs, which have a rail beside the
    ///     page) or centres in the landing page's column.
    /// </summary>
    public required bool FullBleed { get; set; }

    // Same three utilities the landing page's bar always carried, plus the notch. env(safe-area-inset-top)
    // is 0 everywhere that has no notch, so this is identical to what shipped on every other device —
    // and on a phone in portrait it is what keeps the wordmark out from under the status bar. The h-16
    // row below stays 4rem either way, which is what --nav-h (global.css) tells the docs sidebar to
    // clear.
    private const string Bar =
        "app-navbar sticky top-0 z-50 border-b border-ui-line bg-ui-bg/85 text-ui-ink backdrop-blur "
        + "pt-[env(safe-area-inset-top)]";

    private const string NavItemClass =
        "min-h-11 items-center gap-1 rounded-lg px-2 text-ui-muted no-underline "
        + "hover:bg-ui-well hover:text-ui-ink";

    // The row, joined to each container at COMPILE time — constant + constant is folded by the compiler,
    // where an interpolation here would build the same string again on every render of every page.
    private const string Row = "flex h-16 items-center justify-between gap-3";

    private const string WrapRow = SiteLayout.Wrap + " " + Row;

    private const string FullBleedRow = SiteLayout.FullBleed + " " + Row;

    // Hidden below sm, like the "Docs" link beside it: the bar carries a hamburger, a wordmark, two links
    // and the theme picker, and on a 390px screen the docs bar used to measure 399px and scroll the whole
    // document sideways on every page.
    private const string VersionBadge =
        "hidden shrink-0 rounded-full border border-ui-line px-2 py-0.5 text-xs font-medium "
        + "text-ui-muted sm:inline-block";

    protected override Component? Render() =>
        // A <header> with a <nav> inside it, which is what this is. The docs bar used to be a bare <nav>
        // carrying daisyUI's `navbar`; `app-navbar` and `app-brand` survive as the hooks the E2E selects
        // on (and style nothing), so those selectors keep working — as `header.app-navbar` now.
        Header.Class(Bar)[
            Div.Class(FullBleed ? FullBleedRow : WrapRow)[
                Div.Class("flex min-w-0 items-center gap-2")[
                    Leading,
                    // The mark is a link to the front door on both pages — the one convention every
                    // visitor already knows, and the way back to `/` that the docs never had.
                    NavLink
                        .Href(PageMeta.LinkTo(Pages.Routes.HomePage()))
                        .ActiveClass("")
                        .Class("app-brand inline-flex min-w-0 items-center gap-2 text-lg font-semibold "
                               + "tracking-tight text-ui-ink no-underline")[
                        RaskLogo.Size(22).GradientId("brandBolt"),
                        Span.Class("truncate")["Rask"]
                    ],
                    // Outside the link: the version is a statement about the build, not a second name for
                    // the front door. What it says is SiteIdentity.Version's decision, not this bar's.
                    Span.Class(VersionBadge)[$"v{SiteIdentity.Version}"]
                ],
                Nav.Class("flex shrink-0 items-center gap-1 text-sm sm:gap-2")[
                    NavItem("Docs", Features.Routes.GuidesIndexPage(), hideOnPhone: true),
                    ExternalNavItem("GitHub", SiteIdentity.Repository),
                    Ui.ThemeDropdown.Align(Ui.Align.End)
                ]
            ]
        ];

    /// <summary>
    ///     A link to another page OF THIS APP — so it stays in the tab and navigates as an SPA.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>NavLink, not A, and that is the whole difference.</b> The runtime intercepts clicks on
    ///         <c>a[data-rask-nav]</c>, and NavLink is what writes that attribute — a bare
    ///         <c>&lt;a href&gt;</c> is a plain document navigation no matter how internal its URL is, so
    ///         the front door's own Docs link once cold-booted the entire WASM app: several MB of
    ///         runtime, a boot screen, and the hydration reflow all over again. It also takes a type-safe
    ///         <c>RouteUrl</c>, so a renamed route is a build error rather than a dead link.
    ///         <c>ActiveClass("")</c> opts out of active styling: this is chrome, not a section nav.
    ///     </para>
    ///     <para>
    ///         Hidden on a narrow viewport rather than wrapped: the bar is chrome, and links stacking over
    ///         two lines push the hero below the fold on a phone.
    ///     </para>
    /// </remarks>
    private static Component NavItem(string label, RouteUrl href, bool hideOnPhone) =>
        NavLink
            .Href(PageMeta.LinkTo(href))
            .ActiveClass("")
            .Class((hideOnPhone ? "hidden sm:inline-flex " : "inline-flex ") + NavItemClass)[label];

    /// <summary>A link that leaves the site — new tab, and it says so.</summary>
    private static Component ExternalNavItem(string label, string href) =>
        A
            .Class("inline-flex " + NavItemClass)
            .Href(href)
            .Target("_blank")
            .Rel("noopener")[
            label,
            Ui.Icon.Name(Ui.IconName.ExternalLink).Class("size-3.5 shrink-0 opacity-60")
        ];
}
