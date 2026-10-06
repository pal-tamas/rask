namespace Rask;

/// <summary>
/// A line between sections of content or groups of items, optionally with a word on it — Flux UI's separator.
/// </summary>
/// <remarks>
/// <para>
/// It carries no margin: the page spaces it. A vertical one is as tall as the row it stands in, and vertical
/// margin shortens it — <c>Ui.Separator.Vertical().Class("my-2")</c>.
/// </para>
/// <para>
/// Decoration to assistive tech (<c>role="none"</c>), as Flux draws it: what it separates is already told
/// apart by the structure around it.
/// </para>
/// </remarks>
public sealed partial class UiSeparator : Component
{
    /// <summary>Runs down instead of across.</summary>
    public bool? Vertical { get; set; }

    /// <summary>How strongly the line is drawn.</summary>
    public Ui.SeparatorVariant? Variant { get; set; }

    /// <summary>The word in the middle of the line — "or". Omitted, it is a plain line.</summary>
    public string? Text { get; set; }

    /// <summary>The other way to say <see cref="Vertical" />, for a direction decided at run time.</summary>
    public Ui.SeparatorOrientation? Orientation { get; set; }

    public string? Class { get; set; }

    private bool IsVertical => Vertical == true || Orientation == Ui.SeparatorOrientation.Vertical;

    private static string Line(Ui.SeparatorVariant? variant) => variant switch
    {
        Ui.SeparatorVariant.Subtle => "bg-zinc-800/5 dark:bg-white/10",
        _ => "bg-zinc-800/15 dark:bg-white/20",
    };

    /// <inheritdoc />
    protected override Component? Render()
    {
        var root = Div.Data(("orientation", IsVertical ? "vertical" : "horizontal"), ("ui-separator", "")).Role("none");
        if (Text is null)
        {
            return root.Class(UiClass.Compose(IsVertical ? "w-px self-stretch" : "h-px w-full", Line(Variant), Class));
        }

        var line = UiClass.Compose("h-px grow", Line(Variant));
        return root.Class(UiClass.Compose("flex w-full items-center", Class))[
            Div.Class(line),
            Span.Class("mx-6 text-sm font-medium whitespace-nowrap text-zinc-500 dark:text-zinc-300")[Text],
            Div.Class(line)
        ];
    }
}
