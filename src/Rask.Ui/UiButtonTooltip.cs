using Rask.Core;

namespace Rask;

/// <summary>
///     The hint a <see cref="UiButton" /> shows for its <c>Tooltip</c> and <c>Kbd</c> props.
/// </summary>
/// <remarks>
///     A STOPGAP until Flux's tooltip is rebuilt, kept in this one type so that component can replace it.
///     Flux wraps the button in a tooltip element and positions a popover from script; this is a child of the
///     button, shown by CSS while the button is hovered or holds keyboard focus. The bubble itself is drawn
///     as fluxui.dev/components/tooltip measures: 12px medium on zinc-800, 8px by 10px of padding, a 6px
///     radius, 5px from the button.
/// </remarks>
internal static class UiButtonTooltip
{
    // Hidden from assistive tech: the button takes the text as its name when it has no other.
    private static readonly Dictionary<string, string?> Marks = new(StringComparer.Ordinal)
    {
        ["data-ui-tooltip-content"] = null,
        ["role"] = "tooltip",
        ["aria-hidden"] = "true",
    };

    internal static Component Render(string? text, string? kbd, Ui.Position? position) =>
        Markup.Span.Class(Classes(position)).Attributes(Marks)[
            text,
            kbd is null ? null : Markup.Span.Class("text-zinc-300 not-first:ps-1")[kbd]
        ];

    private static string Classes(Ui.Position? position) => position switch
    {
        Ui.Position.Bottom => Bubble + " top-full left-1/2 mt-[5px] -translate-x-1/2",
        Ui.Position.Left => Bubble + " right-full top-1/2 me-[5px] -translate-y-1/2",
        Ui.Position.Right => Bubble + " left-full top-1/2 ms-[5px] -translate-y-1/2",
        _ => Bubble + " bottom-full left-1/2 mb-[5px] -translate-x-1/2",
    };

    private const string Bubble =
        "pointer-events-none absolute z-10 hidden w-max rounded-md bg-zinc-800 px-2.5 py-2 text-xs font-medium text-white "
        + "dark:border dark:border-white/10 dark:bg-zinc-700 [:hover>&]:block [:focus-visible>&]:block";
}
