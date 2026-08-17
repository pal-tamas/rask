namespace Rask.Bootstrap;

// A Bootstrap dropdown driven by Rask's live runtime (no JS). The toggle reuses BsButton; the menu
// shows when Open. Wire OnToggle to flip your Open state, and let each BsDropdownItem's handler set
// Open back to false on selection.

/// <summary>
///     A toggleable menu of actions or links, closing on <c>Escape</c> and on an outside click.
/// </summary>
public sealed partial class BsDropdown : BsBlock
{
    /// <summary>Whether the menu is shown.</summary>
    public bool? Open { get; set; }

    /// <summary>The toggle button's text.</summary>
    public new string? Label { get; set; }

    /// <summary>The toggle's semantic colour.</summary>
    public BsColor? Color { get; set; }

    /// <summary>Draws the toggle outlined rather than filled.</summary>
    public bool? Outline { get; set; }

    /// <summary>Makes the toggle smaller or larger.</summary>
    public BsSize? Size { get; set; }

    // Right-aligns the menu (.dropdown-menu-end).

    /// <summary>Aligns the menu to the toggle's end edge, to stop it overflowing the viewport.</summary>
    public bool? AlignEnd { get; set; }

    /// <summary>Runs when the menu opens or closes.</summary>
    public Action? OnToggle { get; set; }

    /// <summary>Runs when the menu opens or closes, asynchronously.</summary>
    public Func<Task>? OnToggleAsync { get; set; }

    protected override Component? Render()
    {
        var open = Open is true;
        var expanded = new Dictionary<string, string?> { ["expanded"] = open ? "true" : "false" };

        return Div
            .Id(Id)
            .Class(BsClass.Join("dropdown", Class))
            .Data(BsPopover.WrapperFor(AlignEnd is true))[
            BsButton
                .Color(Color)
                .Outline(Outline)
                .Size(Size)
                .Class("dropdown-toggle")
                .Aria(expanded)
                .OnClick(OnToggle)
                .OnClickAsync(OnToggleAsync)[Label ?? ""],
            Ul
                .Class(BsClass.Join("dropdown-menu", AlignEnd is true ? "dropdown-menu-end" : null,
                open ? "show" : null))[Items]];
    }
}

// A dropdown menu entry. Plain item by default; pass Href for a link, OnClick for a button, Header
// for a non-interactive label, or Divider for a separator rule.

/// <summary>
///     One entry in a dropdown: an action, a link, a header, or a divider.
/// </summary>
public sealed partial class BsDropdownItem : BsBlock
{
    /// <summary>Makes the entry a link to this URL.</summary>
    public string? Href { get; set; }

    /// <summary>Renders the entry as the current one.</summary>
    public bool? Active { get; set; }

    /// <summary>Makes the entry non-interactive.</summary>
    public bool? Disabled { get; set; }

    /// <summary>Renders the entry as a non-interactive group heading.</summary>
    public new bool? Header { get; set; }

    /// <summary>Renders the entry as a separator rather than an item.</summary>
    public bool? Divider { get; set; }

    /// <summary>Runs when the entry is chosen.</summary>
    public Action? OnClick { get; set; }

    /// <summary>Runs when the entry is chosen, asynchronously.</summary>
    public Func<Task>? OnClickAsync { get; set; }

    protected override Component? Render()
    {
        if (Divider is true)
        {
            return Li.Id(Id)[Hr.Class("dropdown-divider")];
        }

        if (Header is true)
        {
            return Li.Id(Id)[H6.Class(BsClass.Join("dropdown-header", Class))[Items]];
        }

        var cls = BsClass.Join("dropdown-item",
            Active is true ? "active" : null,
            Disabled is true ? "disabled" : null,
            Class);

        Component item = Href is not null
            ? A.Class(cls).Href(Href)[Items]
            : Button.Type("button").Class(cls).OnClick(OnClick).OnClickAsync(OnClickAsync)[Items];

        return Li.Id(Id)[item];
    }
}
