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
///         Dark mode is <see cref="AppearanceToggle" />, the moon at the trailing edge. The hamburger is a
///         label for the sidebar's checkbox. Neither runs a C# handler.
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

    // The row, joined to each container at COMPILE time — constant + constant is folded by the compiler,
    // where an interpolation here would build the same string again on every render of every page.
    private const string Row = "flex h-16 items-center justify-between gap-3";

    private const string WrapRow = SiteLayout.Wrap + " " + Row;

    private const string FullBleedRow = SiteLayout.FullBleed + " " + Row;

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
                    Ui.Brand
                        .Class("app-brand")
                        .Name("Rask")
                        .Href(PageMeta.LinkTo(Routes.HomePage()))
                        .Logo(RaskLogo.Size(22).GradientId("brandBolt")),
                    // Outside the link: the version is a statement about the build, not a second name for
                    // the front door. What it says is SiteIdentity.Version's decision, not this bar's.
                    // Hidden below sm, like the "Docs" link beside it: on a 390px screen the bar used to
                    // measure 399px and scroll the whole document sideways on every page.
                    Ui.Badge.Class("max-sm:hidden")[$"v{SiteIdentity.Version}"]
                ],
                Div.Class("flex shrink-0 items-center gap-1 sm:gap-2")[
                    Ui.Navbar[
                        Ui.NavbarItem.Href(PageMeta.LinkTo(Routes.GuidesIndexPage())).Current(false).Class("max-sm:hidden")["Docs"],
                        Ui.NavbarItem.Href(SiteIdentity.Repository).IconTrailing(Ui.IconName.ArrowTopRightOnSquare)["GitHub"]
                    ],
                    AppearanceToggle
                ]
            ]
        ];
}
