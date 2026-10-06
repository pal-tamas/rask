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
            : Line("ui-menu-separator");

    /// <summary>
    ///     The line, edge to edge across the menu's padding. <paramref name="marker" /> says which line it is: one
    ///     written between rows, or the one a <see cref="UiMenuGroup" /> draws above or below itself.
    /// </summary>
    internal static Component Line(string marker) =>
        Div.Class("-mx-[.3125rem] my-[.3125rem]").Data(marker, "")[
            Div.Role("none").Class("h-px w-full border-0 bg-zinc-800/15 dark:bg-zinc-600").Data("orientation", "horizontal")
        ];
}
