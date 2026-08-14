namespace Rask.Bootstrap;

// A Bootstrap navbar: <nav class="navbar">. By default it wraps its children in a .container-fluid (set
// Container: false to opt out and lay the children out directly). Expand sets the breakpoint at and
// above which a .navbar-nav lays out horizontally (.navbar-expand-{bp}); Color tints the bar (bg-*);
// Theme emits data-bs-theme so a dark bar gets light text the 5.3 way (no deprecated .navbar-dark);
// Sticky pins it to the top of the viewport (.sticky-top).
public sealed partial class BsNavbar : BsBlock
{
    public Bp? Expand { get; set; }
    public BsColor? Color { get; set; }
    public BsTheme? Theme { get; set; }
    public bool? Sticky { get; set; }
    public bool? Container { get; set; }

    protected override Component? Render()
    {
        var cls = BsClass.Join(
            "navbar",
            Expand is { } bp ? $"navbar-expand-{bp.Token()}" : null,
            Color is { } c ? c.Bg() : null,
            Sticky is true ? "sticky-top" : null,
            Class);

        var theme = Theme is { } t
            ? new Dictionary<string, string?> { ["bs-theme"] = t.Value() }
            : null;

        return Container is false
            ? Nav.Id(Id).Class(cls).Data(theme)[Items]
            : Nav.Id(Id).Class(cls).Data(theme)[Div.Class("container-fluid")[Items]];
    }
}
