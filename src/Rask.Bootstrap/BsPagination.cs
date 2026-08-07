namespace Rask.Bootstrap;

// A Bootstrap pagination control: <nav><ul class="pagination">…</ul></nav> holding BsPageItem
// children. Size maps to pagination-sm / pagination-lg.
public sealed class BsPagination : BsBlock
{
    public BsSize? Size { get; set; }

    // Accessible label for the surrounding <nav>; defaults to "Page navigation".
    public new string? Label { get; set; }

    protected override Component? Render()
    {
        var cls = BsClass.Join(
            "pagination",
            Size is { } s && s.Suffix() is { } suffix ? $"pagination-{suffix}" : null,
            Class);

        var navAria = new Dictionary<string, string?> { ["label"] = Label ?? "Page navigation" };
        return Nav(Id: Id, Aria: navAria)[Ul(Class: cls)[Items]];
    }
}

// A pagination item: <li class="page-item"><a class="page-link">…</a></li>. Active marks the
// current page; Disabled greys it. Pass Href for a link, or OnClick to drive paging from C# (zero
// JS — the handler re-renders through the live runtime).
public sealed class BsPageItem : BsBlock
{
    public bool? Active { get; set; }
    public bool? Disabled { get; set; }
    public string? Href { get; set; }
    public Callback? OnClick { get; set; }
    public CallbackAsync? OnClickAsync { get; set; }

    // Extra ARIA attributes for the page link itself — pass aria-label to name an icon-only control
    // (a prev/next arrow whose only child is a decorative BsIcon has no accessible name otherwise).
    // Merged under the component's own aria-disabled, so a disabled labelled item keeps both.
    public IReadOnlyDictionary<string, string?>? Aria { get; set; }

    protected override Component? Render()
    {
        var liCls = BsClass.Join(
            "page-item",
            Active is true ? "active" : null,
            Disabled is true ? "disabled" : null);

        var liAria = Active is true
            ? new Dictionary<string, string?> { ["current"] = "page" }
            : null;

        // .disabled only greys the item and sets pointer-events:none — it stops a mouse but nothing else, so
        // on its own a "disabled" page stays focusable, announces as enabled, and still fires on Enter. The
        // control itself has to carry aria-disabled (Bootstrap's own documented markup for a disabled page
        // link). It goes on the link, not the <li>, because the link is what takes focus.
        //
        // Deliberately aria-disabled rather than the disabled attribute: disabled would drop focus to <body>
        // the moment a page click starts a fetch, throwing away the user's keyboard position. Callers still
        // guard their handlers — see BsDataGrid.GoToPageAsync.
        // Caller aria first (e.g. aria-label naming an arrow), then aria-disabled layered on top so a
        // disabled arrow keeps its name — WithAria copies rather than mutating the caller's dictionary.
        var linkAria = Disabled is true
            ? BsClass.WithAria(Aria, "disabled", "true")
            : Aria;

        Component link = Href is not null
            ? A(Class: "page-link", Href: Href, Aria: linkAria)[Items]
            : Button(Type: "button", Class: "page-link", Aria: linkAria, OnClick: OnClick,
                OnClickAsync: OnClickAsync)[Items];

        return Li(Id: Id, Class: BsClass.Join(liCls, Class), Aria: liAria)[[link]];
    }
}
