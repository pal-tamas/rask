namespace Rask;

/// <summary>
///     The row a page opens with: its title, a line under it, what the page can do at the end of the row, and the trail
///     above it.
/// </summary>
/// <remarks>
///     <code>
///     Ui.PageHeader.Heading("Suppliers").Subheading("Everyone you buy from.")
///         .Breadcrumbs(Ui.Breadcrumbs[Ui.BreadcrumbsItem.Href(Routes.HomePage())["Home"], Ui.BreadcrumbsItem["Suppliers"]])[
///         Ui.Button.Primary.Icon(Ui.IconName.Plus).Href(Routes.NewSupplierPage())["New supplier"]
///     ]
///     </code>
///     <para>
///         Not a Flux component: an addition the owner approved (2026-10-20, #1283), laid out as Flux's own pages lay out a
///         heading and its actions, measured on fluxui.dev's demos. The heading is the page's <c>&lt;h1&gt;</c> in
///         <see cref="Ui.HeadingSize.Xl" /> (the settings demo's title), its <see cref="Subheading" /> 8px under it (the
///         heading page's text). The row wraps, centred, with 8px between the title and the actions, which keep to its end
///         (the account overview's "Past transactions"); on a phone the title takes the whole line and the actions go
///         under it, 16px down. 24px under the row (the table demo's), and the trail 8px above the title.
///     </para>
///     <para>
///         The children are the actions. Each spacing weighs nothing, so a class of the call site's wins:
///         <c>.Class("mb-0")</c>.
///     </para>
/// </remarks>
public sealed partial class UiPageHeader : Component
{
    // The table demo's 24px under the page's top row.
    private const string Root = "[:where(&)]:mb-6";

    // The account overview's heading row: it wraps, centres and spreads, 8px apart.
    private const string Row = "flex flex-wrap items-center justify-between gap-2";

    // The title takes the whole line on a phone, the actions 8px more below it — the overview's on a phone.
    private const string Named = "min-w-0 max-sm:w-full";

    private const string NamedOverActions = "min-w-0 max-sm:w-full max-sm:mb-2";

    // The overview's actions: what is left of the row, at its end, 8px apart; and Flux's row of buttons, which wraps.
    private const string Actions = "flex flex-1 flex-wrap items-center justify-end gap-2";

    /// <summary>The page's title, its <c>&lt;h1&gt;</c>. Required, and taken first: <c>Ui.PageHeader.Heading("Suppliers")</c>.</summary>
    public required string Heading { get; set; }

    /// <summary>
    ///     The title's heading level, 1 to 4. Unset, 1: the page's <c>&lt;h1&gt;</c>. A header shown on a page that has a
    ///     title of its own (a demo, a section) takes 2.
    /// </summary>
    public int? Level { get; set; }

    /// <summary>A line under the title, in the muted ink.</summary>
    public string? Subheading { get; set; }

    /// <summary>The trail to this page, above the title: a <see cref="UiBreadcrumbs" />.</summary>
    public Component? Breadcrumbs { get; set; }

    /// <summary>Classes for the call site, added to the header's own: <c>mb-0</c>, <c>mb-8</c>.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var actions = Children?.Any(child => child is not null) is true ? Children : null;

        return Div.Class(Root, Class).Data("ui-page-header", "")[
            Breadcrumbs is null ? null : Div.Class("mb-2")[Breadcrumbs],
            Div.Class(Row)[
                Div.Class(actions is null ? Named : NamedOverActions)[
                    Ui.Heading.Level(Level ?? 1).Xl[Heading],
                    Subheading is null ? null : Ui.Subheading[Subheading]
                ],
                actions is null ? null : Div.Class(Actions)[actions]
            ]
        ];
    }
}
