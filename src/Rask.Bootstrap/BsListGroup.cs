namespace Rask.Bootstrap;

// A Bootstrap list group: <ul class="list-group"> holding BsListGroupItem children. Set Flush to
// remove outer borders/rounding, or Numbered for an ordered, auto-numbered list (<ol>).
public sealed class BsListGroup : BsBlock
{
    public bool? Flush { get; set; }
    public bool? Numbered { get; set; }

    protected override Component? Render()
    {
        var cls = BsClass.Join(
            "list-group",
            Flush is true ? "list-group-flush" : null,
            Numbered is true ? "list-group-numbered" : null,
            Class);

        return Numbered is true
            ? Ol(Id: Id, Class: cls)[Items]
            : Ul(Id: Id, Class: cls)[Items];
    }
}

// A list-group item: <li class="list-group-item">. Active marks the current item; Disabled greys it;
// Color tints it (list-group-item-{color}). For a clickable item pass Href — it renders an anchor
// with .list-group-item-action.
public sealed class BsListGroupItem : BsBlock
{
    public bool? Active { get; set; }
    public bool? Disabled { get; set; }
    public BsColor? Color { get; set; }
    public string? Href { get; set; }

    protected override Component? Render()
    {
        var action = Href is not null;
        var cls = BsClass.Join(
            "list-group-item",
            action ? "list-group-item-action" : null,
            Active is true ? "active" : null,
            Disabled is true ? "disabled" : null,
            Color is { } c ? c.ListGroupItem() : null,
            Class);

        var aria = Active is true
            ? new Dictionary<string, string?> { ["current"] = "true" }
            : null;

        return action
            ? A(Id: Id, Class: cls, Href: Href, Aria: aria)[Items]
            : Li(Id: Id, Class: cls, Aria: aria)[Items];
    }
}
