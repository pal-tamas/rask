namespace Rask.Dashboard.Pages;

/// <summary>One figure: what it counts, the number, and a line under it — in a card of its own unless it sits in one.</summary>
/// <remarks>
/// Flux's dashboard figure: a line of text, the value as a heading under it, and Flux's text below. A colour
/// paints the line under the value, which is where the reason a figure is worth noticing is written.
/// </remarks>
internal sealed partial class DashboardMetric : Component
{
    public required string Label { get; set; }

    public string? Value { get; set; }

    public string? Caption { get; set; }

    /// <summary>Something is wrong: painted red.</summary>
    public bool? Danger { get; set; }

    /// <summary>Worth a look: painted amber.</summary>
    public bool? Warning { get; set; }

    public Ui.IconName? Icon { get; set; }

    /// <summary>Drawn without a card, for a figure inside a card already.</summary>
    public bool? Bare { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        Component figure = [
            Div.Class("flex items-center justify-between gap-2")[
                Ui.Text[Label],
                Icon is { } icon ? Ui.Icon.Name(icon).Variant(Ui.IconVariant.Mini).Class("text-zinc-400 dark:text-white/50") : null
            ],
            Ui.Heading.Xl.Class("mt-2 tabular-nums")[Value],
            Caption is null ? null : Ui.Text.Size(Ui.TextSize.Sm).Color(Ink).Class("mt-1")[Caption]
        ];

        return Bare is true ? Div[figure] : Ui.Card[figure];
    }

    private Ui.Color? Ink => (Danger, Warning) switch
    {
        (true, _) => Ui.Color.Red,
        (_, true) => Ui.Color.Amber,
        _ => null,
    };
}
