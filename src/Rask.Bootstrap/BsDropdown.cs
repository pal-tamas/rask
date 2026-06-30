namespace Rask.Bootstrap;

// A Bootstrap dropdown driven by Rask's live runtime (no JS). The toggle reuses BsButton; the menu
// shows when Open. Wire OnToggle to flip your Open state, and let each BsDropdownItem's handler set
// Open back to false on selection.
public sealed class BsDropdown : BsBlock
{
    public bool? Open { get; set; }
    public string? Label { get; set; }
    public BsColor? Color { get; set; }
    public bool? Outline { get; set; }
    public BsSize? Size { get; set; }

    // Right-aligns the menu (.dropdown-menu-end).
    public bool? AlignEnd { get; set; }

    public Callback? OnToggle { get; set; }
    public CallbackAsync? OnToggleAsync { get; set; }

    protected override RenderResult Render()
    {
        var open = Open is true;
        var expanded = new Dictionary<string, string?> { ["expanded"] = open ? "true" : "false" };

        return Div(Id: Id, Class: BsClass.Join("dropdown", Class))[
            BsButton(Color: Color, Outline: Outline, Size: Size, Class: "dropdown-toggle",
                Aria: expanded, OnClick: OnToggle, OnClickAsync: OnToggleAsync)[Label ?? ""],
            Ul(Class: BsClass.Join("dropdown-menu", AlignEnd is true ? "dropdown-menu-end" : null,
                open ? "show" : null))[Items]];
    }
}

// A dropdown menu entry. Plain item by default; pass Href for a link, OnClick for a button, Header
// for a non-interactive label, or Divider for a separator rule.
public sealed class BsDropdownItem : BsBlock
{
    public string? Href { get; set; }
    public bool? Active { get; set; }
    public bool? Disabled { get; set; }
    public bool? Header { get; set; }
    public bool? Divider { get; set; }
    public Callback? OnClick { get; set; }
    public CallbackAsync? OnClickAsync { get; set; }

    protected override RenderResult Render()
    {
        if (Divider is true)
        {
            return Li(Id: Id)[Hr(Class: "dropdown-divider")];
        }

        if (Header is true)
        {
            return Li(Id: Id)[H6(Class: BsClass.Join("dropdown-header", Class))[Items]];
        }

        var cls = BsClass.Join("dropdown-item",
            Active is true ? "active" : null,
            Disabled is true ? "disabled" : null,
            Class);

        Child item = Href is not null
            ? A(Class: cls, Href: Href)[Items]
            : Button(Type: "button", Class: cls, OnClick: OnClick, OnClickAsync: OnClickAsync)[Items];

        return Li(Id: Id)[item];
    }
}
