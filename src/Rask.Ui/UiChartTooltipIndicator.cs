namespace Rask;

/// <summary>
/// A dot in the colour of the hovered pie slice, put inside a <see cref="UiChartTooltipValue" />.
/// Flux UI's <c>chart.tooltip.indicator</c>.
/// </summary>
public sealed partial class UiChartTooltipIndicator : Component
{
    public string? Class { get; set; }

    private const string Dot = "size-2.5 rounded-full";

    // Which slices there are is the chart's rows, which arrive through the context.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    // Only a pie sets the colour, as Flux's. The dot is there once per slice, and the plot hook lights the
    // hovered slice's; the plain one is what shows while no slice is.
    /// <inheritdoc />
    protected override Component? Render()
    {
        var plain = Div.Class(UiClass.Compose(Dot, Class));
        if (Context.Get<UiChartScope>() is not { Pie: { } pie, Data: { } data })
        {
            return plain;
        }

        var dots = new List<Component?> { Div.Key(-1).Class(UiClass.Compose(Dot, "in-data-active:hidden", Class)) };
        var slice = 0;
        for (var row = 0; row < data.Count; row++)
        {
            if (UiChartPieDrawing.Size(pie, data, row) > 0)
            {
                dots.Add(Div.Key(row).Class(UiClass.Compose(Dot, "hidden data-active:block", UiChartPieDrawing.Background(UiChartPieDrawing.Hue(pie, data, row, slice++)), Class))
                    .Data(UiChartHover.Row(row)));
            }
        }

        return RaskFragment([.. dots]);
    }
}
