namespace Rask;

/// <summary>
/// A line between runs of rows. Flux UI's <c>flux:menu.separator</c>.
/// </summary>
/// <remarks>
/// Not a row: the keyboard cursor never lands on it.
/// </remarks>
public sealed partial class UiMenuSeparator : Component
{
    /// <summary>The box a menu's line sits in: edge to edge across the menu's padding, a margin above and below.</summary>
    internal const string Box = "-mx-[.3125rem] my-[.3125rem]";

    /// <inheritdoc />
    protected override Component? Render() => Line();

    /// <summary>
    ///     The line, edge to edge across the menu's padding: Flux's separator inside a marked box.
    ///     <paramref name="edge" /> is the second marker of a line a <see cref="UiMenuGroup" /> draws above
    ///     (<c>ui-menu-separator-top</c>) or below itself.
    /// </summary>
    internal static Component Line(string? edge = null)
    {
        var box = Div.Class(Box);
        return Ruled(edge is null ? box.Data("ui-menu-separator", "") : box.Data(("ui-menu-separator", ""), (edge, "")));
    }

    /// <summary>Flux's separator inside <paramref name="box" />, in the menu's own dark ink (zinc-600 where a page's line is white/20).</summary>
    internal static Component Ruled(Element box) =>
        box[
            Div.Role("none").Class("h-px w-full border-0 bg-zinc-800/15 dark:bg-zinc-600")
                .Data(("orientation", "horizontal"), ("ui-separator", ""))
        ];
}
