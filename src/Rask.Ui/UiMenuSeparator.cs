namespace Rask;

/// <summary>
/// A line between runs of rows. Flux UI's <c>flux:menu.separator</c>.
/// </summary>
/// <remarks>
/// Not a row: the keyboard cursor never lands on it.
/// </remarks>
public sealed partial class UiMenuSeparator : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        Context.Get<UiMenuLevel>() is { Scope.AsOptions: true } palette
            ? UiCommandRows.Separator(palette)
            : Line();

    /// <summary>
    ///     The line, edge to edge across the menu's padding: Flux's separator inside a marked box.
    ///     <paramref name="edge" /> is the second marker of a line a <see cref="UiMenuGroup" /> draws above
    ///     (<c>ui-menu-separator-top</c>) or below itself.
    /// </summary>
    internal static Component Line(string? edge = null)
    {
        var box = Div.Class("-mx-[.3125rem] my-[.3125rem]");
        return (edge is null ? box.Data("ui-menu-separator", "") : box.Data(("ui-menu-separator", ""), (edge, "")))[
            // Flux's separator, in the menu's own dark ink (zinc-600 where a page's line is white/20).
            Div.Role("none").Class("h-px w-full border-0 bg-zinc-800/15 dark:bg-zinc-600")
                .Data(("orientation", "horizontal"), ("ui-separator", ""))
        ];
    }
}
