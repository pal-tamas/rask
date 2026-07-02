namespace Rask.Bootstrap;

// A Bootstrap pagination control: <nav><ul class="pagination">…</ul></nav> holding BsPageItem
// children. Size maps to pagination-sm / pagination-lg.
public sealed class BsPagination : BsBlock
{
    public BsSize? Size { get; set; }

    // Accessible label for the surrounding <nav>; defaults to "Page navigation".
    public string? Label { get; set; }

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

    protected override Component? Render()
    {
        var liCls = BsClass.Join(
            "page-item",
            Active is true ? "active" : null,
            Disabled is true ? "disabled" : null);

        var liAria = Active is true
            ? new Dictionary<string, string?> { ["current"] = "page" }
            : null;

        Component link = Href is not null
            ? A(Class: "page-link", Href: Href)[Items]
            : Button(Type: "button", Class: "page-link", OnClick: OnClick, OnClickAsync: OnClickAsync)[Items];

        return Li(Id: Id, Class: BsClass.Join(liCls, Class), Aria: liAria)[[link]];
    }
}
