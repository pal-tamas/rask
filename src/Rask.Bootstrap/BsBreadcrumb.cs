namespace Rask.Bootstrap;

// A Bootstrap breadcrumb: <nav aria-label="breadcrumb"><ol class="breadcrumb">…</ol></nav> holding
// BsBreadcrumbItem children.

/// <summary>
///     A trail showing where the current page sits in the hierarchy.
/// </summary>
public sealed partial class BsBreadcrumb : BsBlock
{
    // Accessible label for the surrounding <nav>; defaults to "breadcrumb".
    public new string? Label { get; set; }

    protected override Component? Render()
    {
        var navAria = new Dictionary<string, string?> { ["label"] = Label ?? "breadcrumb" };
        return Nav.Id(Id).Aria(navAria)[Ol.Class(BsClass.Join("breadcrumb", Class))[Items]];
    }
}

// A breadcrumb item: <li class="breadcrumb-item">. Pass Href for a link; mark the current page with
// Active (renders plain text + aria-current="page").

/// <summary>
///     One step in a breadcrumb trail. The last one is the current page and should be marked <c>Active</c>
///     rather than linked.
/// </summary>
public sealed partial class BsBreadcrumbItem : BsBlock
{
    /// <summary>Marks this the current page, which renders it unlinked.</summary>
    public bool? Active { get; set; }

    /// <summary>Where the step links to.</summary>
    public string? Href { get; set; }

    protected override Component? Render()
    {
        var active = Active is true;
        var cls = BsClass.Join("breadcrumb-item", active ? "active" : null, Class);
        var aria = active ? new Dictionary<string, string?> { ["current"] = "page" } : null;

        return Href is not null && !active
            ? Li.Id(Id).Class(cls).Aria(aria)[A.Href(Href)[Items]]
            : Li.Id(Id).Class(cls).Aria(aria)[Items];
    }
}
