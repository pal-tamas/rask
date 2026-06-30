namespace Rask.Bootstrap;

// A Bootstrap breadcrumb: <nav aria-label="breadcrumb"><ol class="breadcrumb">…</ol></nav> holding
// BsBreadcrumbItem children.
public sealed class BsBreadcrumb : BsBlock
{
    // Accessible label for the surrounding <nav>; defaults to "breadcrumb".
    public string? Label { get; set; }

    protected override RenderResult Render()
    {
        var navAria = new Dictionary<string, string?> { ["label"] = Label ?? "breadcrumb" };
        return Nav(Id: Id, Aria: navAria)[Ol(Class: BsClass.Join("breadcrumb", Class))[Items]];
    }
}

// A breadcrumb item: <li class="breadcrumb-item">. Pass Href for a link; mark the current page with
// Active (renders plain text + aria-current="page").
public sealed class BsBreadcrumbItem : BsBlock
{
    public bool? Active { get; set; }
    public string? Href { get; set; }

    protected override RenderResult Render()
    {
        var active = Active is true;
        var cls = BsClass.Join("breadcrumb-item", active ? "active" : null, Class);
        var aria = active ? new Dictionary<string, string?> { ["current"] = "page" } : null;

        return Href is not null && !active
            ? Li(Id: Id, Class: cls, Aria: aria)[A(Href: Href)[Items]]
            : Li(Id: Id, Class: cls, Aria: aria)[Items];
    }
}
