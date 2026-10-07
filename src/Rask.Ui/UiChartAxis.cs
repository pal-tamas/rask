namespace Rask;

/// <summary>
/// One axis of a chart, and what is drawn along it: put a <see cref="UiChartAxisTick" />,
/// <see cref="UiChartAxisGrid" />, <see cref="UiChartAxisLine" /> or <see cref="UiChartAxisMark" /> inside.
/// Flux UI's <c>chart.axis</c>.
/// </summary>
/// <remarks>
/// The axis that carries a <see cref="Field" /> is the chart's index: dates are spread over time, anything else
/// evenly, and bars take a band each. The other axis is the value axis, linear from zero.
/// </remarks>
public sealed partial class UiChartAxis : Component, IUiChartField
{
    /// <summary>Which axis this configures.</summary>
    public Ui.ChartAxisAxis? Axis { get; set; }

    /// <summary>What names each row along the index axis: <c>.Field((Visit v) =&gt; v.Date)</c>.</summary>
    public UiChartField? Field { get; set; }

    /// <summary>How the tick labels are written.</summary>
    public UiChartFormat? Format { get; set; }

    /// <summary>Which side of the plot the axis sits on: <see cref="Ui.Position.Right" /> for a value axis on the right.</summary>
    public Ui.Position? Position { get; set; }

    /// <summary>About how many ticks the axis has. Five on a value axis; every row on a band axis.</summary>
    public int? TickCount { get; set; }

    /// <summary>Where the value axis starts. Zero, or the lowest value when one is below zero, unless this says.</summary>
    public double? TickStart { get; set; }

    /// <summary>Where the value axis ends. The highest value, or the tick just past it, unless this says.</summary>
    public double? TickEnd { get; set; }

    /// <summary>The ticks themselves, in place of the round ones the axis would choose.</summary>
    public IReadOnlyList<double>? TickValues { get; set; }

    /// <summary>Written before every tick label: <c>"$"</c>.</summary>
    public string? TickPrefix { get; set; }

    /// <summary>Written after every tick label: <c>"MB"</c>.</summary>
    public string? TickSuffix { get; set; }

    internal UiChartAxisTick? Tick { get; private set; }

    internal UiChartAxisGrid? Grid { get; private set; }

    internal UiChartAxisLine? Line { get; private set; }

    internal UiChartAxisMark? Mark { get; private set; }

    internal UiChartFormat? Formatting => Tick?.Format ?? Format;

    // Drawn by the UiChartSvg it is declared in.
    /// <inheritdoc />
    protected override Component? Render() => null;

    /// <summary>Reads the parts declared inside, once per render, before the chart is laid out.</summary>
    internal void Resolve()
    {
        foreach (var child in Children ?? [])
        {
            switch (child)
            {
                case UiChartAxisTick tick:
                    Tick = tick;
                    break;
                case UiChartAxisGrid grid:
                    Grid = grid;
                    break;
                case UiChartAxisLine line:
                    Line = line;
                    break;
                case UiChartAxisMark mark:
                    Mark = mark;
                    break;
                default:
                    break;
            }
        }
    }
}
