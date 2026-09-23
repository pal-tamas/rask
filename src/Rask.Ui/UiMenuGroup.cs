namespace Rask;

/// <summary>
/// A run of related items under a heading.
/// </summary>
/// <remarks>
/// The items stay at the menu's own level rather than being nested a step in, so the arrow keys move straight
/// through the group as if the heading were not there — the heading labels a run of rows, it does not open
/// anything. It is daisyUI's <c>menu-title</c>, presentational to assistive tech in a dropdown so the menu reads as
/// one list of items.
/// </remarks>
public sealed partial class UiMenuGroup : Component
{
    /// <summary>The words above the items. Omit it for a group that is only set apart by separators.</summary>
    public string? Heading { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var inMenu = Context.Get<UiMenuLevel>() is not null;
        return
        [
            Heading is null
                ? null
                : Li.Class(UiClass.Compose("menu-title", Class)).Role(inMenu ? "presentation" : null)[Heading],
            .. Children ?? []
        ];
    }
}
