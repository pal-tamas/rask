namespace Rask;

/// <summary>What a <see cref="UiChartSvg" /> was given to draw, sorted by what each part is.</summary>
internal sealed class UiChartParts
{
    private readonly UiChartData _data;

    internal UiChartParts(IEnumerable<Component?>? children, UiChartData data)
    {
        _data = data;
        foreach (var child in children ?? [])
        {
            Sort(child);
        }

        Measure();
    }

    /// <summary>Lines, areas, points, bars, groups and stacks, in the order they were declared and are drawn.</summary>
    internal List<Component> Series { get; } = [];

    internal UiChartAxis? X { get; private set; }

    internal UiChartAxis? Y { get; private set; }

    internal UiChartCursor? Cursor { get; private set; }

    internal UiChartZeroLine? ZeroLine { get; private set; }

    internal UiChartPie? Pie { get; private set; }

    internal bool HasBars { get; private set; }

    internal double Lowest { get; private set; }

    internal double Highest { get; private set; }

    internal UiChartAxis? IndexAxis(bool horizontal) => horizontal ? Y : X;

    internal UiChartAxis? ValueAxis(bool horizontal) => horizontal ? X : Y;

    /// <summary>A part's number for a row: its field, or the row itself when the chart holds bare values.</summary>
    internal double Read(UiChartField? field, int row) => field is null ? _data.Number(row) : field.Number(_data, row);

    private void Sort(Component? child)
    {
        switch (child)
        {
            case UiChartAxis axis:
                axis.Resolve();
                if (axis.Axis == Ui.ChartAxisAxis.Y)
                {
                    Y = axis;
                }
                else
                {
                    X = axis;
                }

                break;
            case UiChartCursor cursor:
                Cursor = cursor;
                break;
            case UiChartZeroLine zero:
                ZeroLine = zero;
                break;
            case UiChartPie pie:
                Pie = pie;
                break;
            case UiChartBar or UiChartGroup or UiChartStack:
                HasBars = true;
                Series.Add(child);
                break;
            case UiChartLine or UiChartArea or UiChartPoint:
                Series.Add(child);
                break;
            default:
                break;
        }
    }

    private void Measure()
    {
        Lowest = double.PositiveInfinity;
        Highest = double.NegativeInfinity;
        foreach (var part in Series)
        {
            switch (part)
            {
                case UiChartStack stack:
                    Totals(stack.Bars());
                    break;
                case UiChartGroup group:
                    foreach (var bar in group.Bars())
                    {
                        Each(bar.Field);
                    }

                    break;
                case IUiChartField single:
                    Each(single.Field);
                    break;
                default:
                    break;
            }
        }

        if (Lowest > Highest)
        {
            (Lowest, Highest) = (0, 0);
        }
    }

    private void Each(UiChartField? field)
    {
        for (var row = 0; row < _data.Count; row++)
        {
            Include(Read(field, row));
        }
    }

    // A stack reaches as high as its bars add up to, row by row.
    private void Totals(List<UiChartBar> bars)
    {
        for (var row = 0; row < _data.Count; row++)
        {
            var total = 0d;
            foreach (var bar in bars)
            {
                var value = Read(bar.Field, row);
                total += double.IsNaN(value) ? 0 : value;
            }

            Include(total);
        }
    }

    private void Include(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return;
        }

        Lowest = Math.Min(Lowest, value);
        Highest = Math.Max(Highest, value);
    }
}
