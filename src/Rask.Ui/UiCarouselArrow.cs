namespace Rask;

/// <summary>
///     One previous or next button of a carousel: the round one over its viewport, or the bordered one of
///     <see cref="UiCarouselControls" />.
/// </summary>
/// <remarks>
///     Rendered for a carousel resting at its start, which is where every carousel is first shown: the
///     previous button is disabled and its wrapper marked <c>data-at-start</c>. The classes read
///     <c>data-at-start</c> and <c>data-at-end</c> off the wrapper, and the runtime's carousel hook moves
///     those and the button's <c>disabled</c> as the track scrolls (<c>data-direction</c> is what it finds
///     the wrapper by).
/// </remarks>
internal static class UiCarouselArrow
{
    private const string Round = "flex size-8 items-center justify-center rounded-full";

    private const string Floating =
        Round + " bg-white shadow-xs ring-1 ring-black/5 hover:bg-zinc-50 dark:bg-zinc-800 dark:ring-white/10 dark:hover:bg-zinc-700 dark:hover:ring-white/20";

    private const string Bordered =
        Round + " border border-zinc-200 bg-white hover:bg-zinc-50 dark:border-white/10 dark:bg-zinc-700 dark:hover:bg-zinc-600/75";

    private static readonly Dictionary<string, string?> PreviousAtStart = new(StringComparer.Ordinal)
    {
        ["direction"] = "previous",
        ["at-start"] = null,
    };

    private static readonly Dictionary<string, string?> NextAtStart = new(StringComparer.Ordinal)
    {
        ["direction"] = "next",
        ["at-start"] = null,
    };

    /// <summary>An arrow over the viewport. It goes away at the end it points to.</summary>
    internal static Component Over(bool next, string position, string? arrowsClass, bool disabled) =>
        Wrapper(
            next,
            UiClass.Compose(
                "absolute top-1/2 -translate-y-1/2 transition-opacity duration-200",
                next ? "data-at-end:pointer-events-none data-at-end:opacity-0" : "data-at-start:pointer-events-none data-at-start:opacity-0",
                position),
            UiClass.Compose(Floating, arrowsClass),
            "text-zinc-700 dark:text-zinc-200",
            disabled);

    /// <summary>An arrow of the external controls. It dims at the end it points to.</summary>
    internal static Component Beside(bool next) =>
        Wrapper(
            next,
            next
                ? "transition-opacity duration-200 data-at-end:pointer-events-none data-at-end:opacity-50"
                : "transition-opacity duration-200 data-at-start:pointer-events-none data-at-start:opacity-50",
            Bordered,
            "text-zinc-800 dark:text-zinc-200",
            disabled: false);

    private static Component Wrapper(bool next, string wrapper, string button, string ink, bool disabled) =>
        Markup.Div.Key(next ? "next" : "previous").Class(wrapper).Data(next ? NextAtStart : PreviousAtStart)[
            Markup.Button
                .Type(ButtonType.Button)
                .Aria("label", next
                    ? RaskStrings.Get(RaskString.CarouselNext, "Next slide")
                    : RaskStrings.Get(RaskString.CarouselPrevious, "Previous slide"))
                .Disabled(disabled || !next)
                .Class(button)[
                Ui.Icon.Name(next ? Ui.IconName.ChevronRight : Ui.IconName.ChevronLeft).Variant(Ui.IconVariant.Mini).Class(ink)
            ]
        ];
}
