namespace Rask;

/// <summary>
///     Flux's <c>flux:timeline.indicator</c>: the circle on the line, holding an icon, a number or a word.
/// </summary>
/// <remarks>
///     <para>
///     What it is drawn as is decided in this order: <see cref="Ui.TimelineIndicatorVariant.Bare" /> draws
///     nothing of its own; then a status — its own, else its item's; then a <see cref="Color" />; then the
///     plain grey circle, whose text is darker in a large timeline.
///     </para>
///     <para>
///     Beside its content it carries a hidden, empty first line (<c>data-ui-timeline-baseline</c>). That line
///     is what <see cref="Ui.TimelineAlign.Baseline" /> lines the content up with, so an indicator holding an
///     icon has a baseline too; give it the content's font size when that is not the default.
///     </para>
/// </remarks>
public sealed partial class UiTimelineIndicator : Component
{
    private const string Bare = "[:where(&)]:grid place-items-center [:where(&)]:rounded-full [:where(&)]:text-sm font-semibold";

    private const string Plain =
        "[:where(&)]:grid [:where(&)]:size-(--ui-timeline-indicator-size) place-items-center [:where(&)]:rounded-full bg-zinc-100 [:where(&)]:text-sm font-semibold [:where(&)]:text-zinc-500 "
        + "dark:bg-zinc-700 dark:text-zinc-300";

    private const string PlainLarge =
        "[:where(&)]:grid [:where(&)]:size-(--ui-timeline-indicator-size) place-items-center [:where(&)]:rounded-full bg-zinc-100 [:where(&)]:text-sm font-semibold [:where(&)]:text-zinc-800 "
        + "dark:bg-zinc-700 dark:text-white";

    private const string Complete =
        "[:where(&)]:grid [:where(&)]:size-(--ui-timeline-indicator-size) place-items-center [:where(&)]:rounded-full bg-zinc-800 [:where(&)]:text-sm font-semibold [:where(&)]:text-white "
        + "dark:bg-white dark:text-zinc-800";

    private const string Current =
        "[:where(&)]:grid [:where(&)]:size-(--ui-timeline-indicator-size) place-items-center [:where(&)]:rounded-full border-2 border-zinc-800 [:where(&)]:text-sm font-semibold "
        + "[:where(&)]:text-zinc-800 dark:border-white dark:text-zinc-300";

    private const string Incomplete =
        "[:where(&)]:grid [:where(&)]:size-(--ui-timeline-indicator-size) place-items-center [:where(&)]:rounded-full border-2 border-zinc-100 [:where(&)]:text-sm font-semibold "
        + "[:where(&)]:text-zinc-500 dark:border-zinc-700 dark:text-zinc-300";

    // Zero-width space: a line box with a baseline and no width.
    private const string EmptyLine = "\u200B";

    /// <summary>Bare strips the circle and its size, for a larger icon standing on its own.</summary>
    public Ui.TimelineIndicatorVariant? Variant { get; set; }

    /// <summary>The status to draw, when not the item's.</summary>
    public Ui.TimelineStatus? Status { get; set; }

    /// <summary>A coloured circle. A status, the indicator's or its item's, is drawn instead of it.</summary>
    public Ui.Color? Color { get; set; }

    /// <summary>Classes for the call site, added to the indicator's own.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var scope = Context.Get<UiTimelineScope>();

        return Div.Data("ui-timeline-indicator").Class(Look(scope), Class)[
            Div.Data("ui-timeline-baseline").Aria("hidden", "true")[EmptyLine],
            Div[Children ?? []]
        ];
    }

    private string Look(UiTimelineScope? scope)
    {
        if (Variant == Ui.TimelineIndicatorVariant.Bare)
        {
            return Bare;
        }

        return (Status ?? scope?.Status ?? Ui.TimelineStatus.Default) switch
        {
            Ui.TimelineStatus.Complete => Complete,
            Ui.TimelineStatus.Current => Current,
            Ui.TimelineStatus.Incomplete => Incomplete,
            _ when Color is { } color && Filled(color) is { } filled => filled,
            _ => scope?.Size == Ui.TimelineSize.Lg ? PlainLarge : Plain,
        };
    }

    // Flux's solid colours: the 500 of the hue, the 600 in dark; the three light hues keep their fill in dark
    // and take dark text. The greys of Ui.Color are not colours Flux gives an indicator, and draw the plain one.
    private static string? Filled(Ui.Color color) => color switch
    {
        Ui.Color.Red => "[:where(&)]:grid [:where(&)]:size-(--ui-timeline-indicator-size) place-items-center [:where(&)]:rounded-full bg-red-500 [:where(&)]:text-sm font-semibold [:where(&)]:text-white dark:bg-red-600",
        Ui.Color.Orange => "[:where(&)]:grid [:where(&)]:size-(--ui-timeline-indicator-size) place-items-center [:where(&)]:rounded-full bg-orange-500 [:where(&)]:text-sm font-semibold [:where(&)]:text-white dark:bg-orange-600",
        Ui.Color.Amber => "[:where(&)]:grid [:where(&)]:size-(--ui-timeline-indicator-size) place-items-center [:where(&)]:rounded-full bg-amber-500 [:where(&)]:text-sm font-semibold [:where(&)]:text-white dark:text-zinc-950",
        Ui.Color.Yellow => "[:where(&)]:grid [:where(&)]:size-(--ui-timeline-indicator-size) place-items-center [:where(&)]:rounded-full bg-yellow-500 [:where(&)]:text-sm font-semibold [:where(&)]:text-white dark:bg-yellow-400 dark:text-zinc-950",
        Ui.Color.Lime => "[:where(&)]:grid [:where(&)]:size-(--ui-timeline-indicator-size) place-items-center [:where(&)]:rounded-full bg-lime-500 [:where(&)]:text-sm font-semibold [:where(&)]:text-white dark:bg-lime-600",
        Ui.Color.Green => "[:where(&)]:grid [:where(&)]:size-(--ui-timeline-indicator-size) place-items-center [:where(&)]:rounded-full bg-green-500 [:where(&)]:text-sm font-semibold [:where(&)]:text-white dark:bg-green-600",
        Ui.Color.Emerald => "[:where(&)]:grid [:where(&)]:size-(--ui-timeline-indicator-size) place-items-center [:where(&)]:rounded-full bg-emerald-500 [:where(&)]:text-sm font-semibold [:where(&)]:text-white dark:bg-emerald-600",
        Ui.Color.Teal => "[:where(&)]:grid [:where(&)]:size-(--ui-timeline-indicator-size) place-items-center [:where(&)]:rounded-full bg-teal-500 [:where(&)]:text-sm font-semibold [:where(&)]:text-white dark:bg-teal-600",
        Ui.Color.Cyan => "[:where(&)]:grid [:where(&)]:size-(--ui-timeline-indicator-size) place-items-center [:where(&)]:rounded-full bg-cyan-500 [:where(&)]:text-sm font-semibold [:where(&)]:text-white dark:bg-cyan-600",
        Ui.Color.Sky => "[:where(&)]:grid [:where(&)]:size-(--ui-timeline-indicator-size) place-items-center [:where(&)]:rounded-full bg-sky-500 [:where(&)]:text-sm font-semibold [:where(&)]:text-white dark:bg-sky-600",
        Ui.Color.Blue => "[:where(&)]:grid [:where(&)]:size-(--ui-timeline-indicator-size) place-items-center [:where(&)]:rounded-full bg-blue-500 [:where(&)]:text-sm font-semibold [:where(&)]:text-white dark:bg-blue-600",
        Ui.Color.Indigo => "[:where(&)]:grid [:where(&)]:size-(--ui-timeline-indicator-size) place-items-center [:where(&)]:rounded-full bg-indigo-500 [:where(&)]:text-sm font-semibold [:where(&)]:text-white dark:bg-indigo-600",
        Ui.Color.Violet => "[:where(&)]:grid [:where(&)]:size-(--ui-timeline-indicator-size) place-items-center [:where(&)]:rounded-full bg-violet-500 [:where(&)]:text-sm font-semibold [:where(&)]:text-white dark:bg-violet-600",
        Ui.Color.Purple => "[:where(&)]:grid [:where(&)]:size-(--ui-timeline-indicator-size) place-items-center [:where(&)]:rounded-full bg-purple-500 [:where(&)]:text-sm font-semibold [:where(&)]:text-white dark:bg-purple-600",
        Ui.Color.Fuchsia => "[:where(&)]:grid [:where(&)]:size-(--ui-timeline-indicator-size) place-items-center [:where(&)]:rounded-full bg-fuchsia-500 [:where(&)]:text-sm font-semibold [:where(&)]:text-white dark:bg-fuchsia-600",
        Ui.Color.Pink => "[:where(&)]:grid [:where(&)]:size-(--ui-timeline-indicator-size) place-items-center [:where(&)]:rounded-full bg-pink-500 [:where(&)]:text-sm font-semibold [:where(&)]:text-white dark:bg-pink-600",
        Ui.Color.Rose => "[:where(&)]:grid [:where(&)]:size-(--ui-timeline-indicator-size) place-items-center [:where(&)]:rounded-full bg-rose-500 [:where(&)]:text-sm font-semibold [:where(&)]:text-white dark:bg-rose-600",
        _ => null,
    };
}
