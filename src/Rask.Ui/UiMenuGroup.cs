namespace Rask;

/// <summary>
/// A run of related rows, set apart by a line and optionally named. Flux UI's <c>flux:menu.group</c>.
/// </summary>
/// <remarks>
/// The rows stay at the menu's own level, so the arrow keys move straight through the group as if the heading
/// were not there. It draws a line above and below itself, and the stylesheet drops the one that would double
/// up or lead nowhere: above the first thing in the menu or another group, below the last.
/// </remarks>
public sealed partial class UiMenuGroup : Component
{
    /// <summary>The words above the rows. Omit it for a group that is only set apart by its lines.</summary>
    public string? Heading { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        if (Context.Get<UiMenuLevel>() is { Scope.AsOptions: true })
        {
            return [Heading is { } title ? UiCommandRows.Heading(title) : null, .. Children ?? []];
        }

        Component?[] content =
        [
            global::Rask.UiMenuSeparator.Line("ui-menu-separator-top"),
            Heading is { } heading
                ? Div.Class("flex w-full items-center px-2 pt-2 pb-1 text-xs font-medium text-zinc-500 dark:text-zinc-300")
                    .Data("ui-menu-heading", "")[
                    UiMenuRow.Indent(),
                    Div[heading]
                ]
                : null,
            .. Children ?? [],
            global::Rask.UiMenuSeparator.Line("ui-menu-separator-bottom")
        ];

        return Div.Role("group").Class(UiClass.Compose("-mx-[.3125rem] px-[.3125rem]", Class)).Data("ui-menu-group", "")[content];
    }
}
