using System.Globalization;
using System.Runtime.InteropServices;

namespace Rask;

/// <summary>
/// One chart, laid out: where the plot sits in its box, the two scales, and which tick labels show.
/// </summary>
/// <remarks>
/// <para>
/// Flux's layout, as measured. The plot starts as the whole box. Whatever would then reach past an edge — a
/// tick label hanging under the axis, the widest value label beside it, half of the last date, a point's radius
/// — is how far that edge is pulled in, on top of the gutter. Nothing is measured twice: the overflow is taken
/// from that first, full-box pass.
/// </para>
/// <para>
/// Labels along the index axis that would come within 20px of each other give way: dates drop every other
/// one, and then all but the first and last; names turn 45°.
/// </para>
/// </remarks>
internal sealed class UiChartPlot
{
    internal const double LabelHeight = 15;
    internal const double LabelOffset = 12;
    private const double LabelGap = 20;
    private static readonly double Diagonal = Math.Sqrt(0.5);

    private readonly UiChartParts _parts;
    private readonly UiChartData _data;
    private readonly double[] _gutter;

    internal UiChartPlot(UiChartParts parts, UiChartData data, bool horizontal, double width, double height, string? gutter)
    {
        _parts = parts;
        _data = data;
        Horizontal = horizontal;
        Width = width;
        Height = height;
        _gutter = Gutter(gutter);
        Banded = parts.HasBars;
        Units = new double[data.Count];
        Place();
        Values = UiChartValueScale.For(parts.Lowest, parts.Highest, parts.ValueAxis(horizontal));
        ValueLabels = Write(Values.Ticks, parts.ValueAxis(horizontal));
        Settle();
    }

    internal bool Horizontal { get; }

    internal double Width { get; }

    internal double Height { get; }

    /// <summary>Whether rows take a band each (bars) rather than a point each.</summary>
    internal bool Banded { get; }

    /// <summary>Each row's place along the index axis, from 0 at its start to 1 at its end.</summary>
    internal double[] Units { get; }

    internal UiChartValueScale Values { get; }

    internal UiChartLabel[] ValueLabels { get; }

    internal List<UiChartLabel> IndexLabels { get; } = [];

    internal UiChartCrowding Crowding { get; private set; }

    internal double Left { get; private set; }

    internal double Right { get; private set; }

    internal double Top { get; private set; }

    internal double Bottom { get; private set; }

    /// <summary>The share of the index axis one row's band takes.</summary>
    internal double Band => Units.Length == 0 ? 1 : 1d / Units.Length;

    /// <summary>Where along the index axis <paramref name="unit" /> falls, in pixels.</summary>
    internal double Index(double unit) => Horizontal ? Top + (unit * (Bottom - Top)) : Left + (unit * (Right - Left));

    /// <summary>Where on the value axis <paramref name="value" /> falls, in pixels.</summary>
    internal double Value(double value) => Horizontal
        ? Left + (Values.Fraction(value) * (Right - Left))
        : Bottom - (Values.Fraction(value) * (Bottom - Top));

    /// <summary>The value axis's resting line: zero, or the end of the axis nearest it.</summary>
    internal double Baseline => Value(Math.Clamp(0, Values.Low, Values.High));

    // Rows along the index axis: by date when the axis field is one, in a band each under bars, evenly otherwise.
    private void Place()
    {
        var axis = _parts.IndexAxis(Horizontal);
        var field = axis?.Field;
        var count = Units.Length;
        var timed = field?.Kind == UiChartFieldKind.Time && !Banded && count > 0;
        if (timed)
        {
            PlaceInTime(axis!, field!);
            return;
        }

        for (var row = 0; row < count; row++)
        {
            Units[row] = Banded ? (row + 0.5) / count : Evenly(row, count);
        }

        if (field is null || axis!.Tick is null)
        {
            return;
        }

        var every = Banded && axis.TickCount is { } target ? Math.Max(1, count / Math.Max(1, target)) : 1;
        var pattern = field.Kind == UiChartFieldKind.Time && count > 0
            ? UiChartTimeTicks.Pattern(field.Time(_data, 0), field.Time(_data, count - 1))
            : null;
        for (var row = 0; row < count; row += every)
        {
            var text = Decorate(axis, pattern is null ? Named(axis, field, row) : Moment(axis, field.Time(_data, row), pattern));
            IndexLabels.Add(new UiChartLabel(Units[row], text, UiChartInter.Measure(text, medium: !Horizontal)));
        }

        Dated = pattern is not null;
    }

    private bool Dated { get; set; }

    private static double Evenly(int row, int count) => count == 1 ? 0.5 : (double)row / (count - 1);

    private string Named(UiChartAxis axis, UiChartField field, int row) =>
        field.Kind == UiChartFieldKind.Number && axis.Formatting is { } format ? format.Number(field.Number(_data, row)) : field.Text(_data, row);

    private void PlaceInTime(UiChartAxis axis, UiChartField field)
    {
        var first = field.Time(_data, 0);
        var last = first;
        for (var row = 1; row < Units.Length; row++)
        {
            var moment = field.Time(_data, row);
            first = moment < first ? moment : first;
            last = moment > last ? moment : last;
        }

        var span = (last - first).Ticks;
        for (var row = 0; row < Units.Length; row++)
        {
            Units[row] = span == 0 ? 0.5 : (double)(field.Time(_data, row) - first).Ticks / span;
        }

        Dated = true;
        if (axis.Tick is null)
        {
            return;
        }

        var ticks = UiChartTimeTicks.For(first, last, Units.Length);
        var pattern = UiChartTimeTicks.Pattern(ticks[0], ticks[^1]);
        foreach (var tick in ticks)
        {
            var text = Decorate(axis, Moment(axis, tick, pattern));
            IndexLabels.Add(new UiChartLabel(span == 0 ? 0.5 : (double)(tick - first).Ticks / span, text, UiChartInter.Measure(text, medium: !Horizontal)));
        }
    }

    private static string Moment(UiChartAxis axis, DateTime moment, string pattern) =>
        axis.Formatting is { } format ? format.Date(moment) : moment.ToString(pattern, CultureInfo.CurrentCulture).Trim();

    private UiChartLabel[] Write(double[] ticks, UiChartAxis? axis)
    {
        if (axis?.Tick is null)
        {
            return [];
        }

        var labels = new UiChartLabel[ticks.Length];
        for (var i = 0; i < ticks.Length; i++)
        {
            var text = Decorate(axis, axis.Formatting is { } format ? format.Number(ticks[i]) : ticks[i].ToString("#,##0.###", CultureInfo.CurrentCulture));
            labels[i] = new UiChartLabel(Values.Fraction(ticks[i]), text, UiChartInter.Measure(text, medium: Horizontal));
        }

        return labels;
    }

    private static string Decorate(UiChartAxis axis, string text) => axis.TickPrefix + text + axis.TickSuffix;

    // All labels shown; if they crowd, the lighter remedy, then the heavier. Each is a full layout of its own,
    // because a hidden last label no longer holds the plot's edge in.
    private void Settle()
    {
        Inset(UiChartCrowding.None);
        if (Horizontal || !Crowded())
        {
            return;
        }

        if (!Dated)
        {
            Inset(UiChartCrowding.Turned);
            return;
        }

        Inset(UiChartCrowding.EveryOther);
        if (Crowded())
        {
            Inset(UiChartCrowding.Ends);
        }
    }

    private bool Crowded()
    {
        var previous = double.NegativeInfinity;
        var span = CollectionsMarshal.AsSpan(IndexLabels);
        for (var i = 0; i < span.Length; i++)
        {
            if (Hidden(i, span.Length))
            {
                continue;
            }

            var centre = Index(span[i].Unit);
            if (centre - (span[i].Width / 2) - previous < LabelGap)
            {
                return true;
            }

            previous = centre + (span[i].Width / 2);
        }

        return false;
    }

    /// <summary>Whether the index label at <paramref name="index" /> of <paramref name="count" /> has given way.</summary>
    internal bool Hidden(int index, int count) => Crowding switch
    {
        UiChartCrowding.EveryOther => index % 2 == 1,
        UiChartCrowding.Ends => index != 0 && index != count - 1,
        _ => false,
    };

    private void Inset(UiChartCrowding crowding)
    {
        Crowding = crowding;
        var reach = new UiChartReach();
        if (Horizontal)
        {
            ReachAlongSide(ref reach, CollectionsMarshal.AsSpan(IndexLabels), fromTop: true, right: false);
            ReachAlongBottom(ref reach, ValueLabels);
        }
        else
        {
            ReachAlongBottom(ref reach, CollectionsMarshal.AsSpan(IndexLabels));
            ReachAlongSide(ref reach, ValueLabels, fromTop: false, _parts.ValueAxis(false)?.Position == Ui.Position.Right);
        }

        ReachOfPoints(ref reach);
        Left = _gutter[3] + reach.Left;
        Right = Width - _gutter[1] - reach.Right;
        Top = _gutter[0] + reach.Top;
        Bottom = Height - _gutter[2] - reach.Bottom;
    }

    // Labels hanging under the plot: a line of text one em below the axis, centred on its tick.
    private void ReachAlongBottom(ref UiChartReach reach, ReadOnlySpan<UiChartLabel> labels)
    {
        if (labels.Length == 0)
        {
            return;
        }

        var widest = 0d;
        for (var i = 0; i < labels.Length; i++)
        {
            widest = Math.Max(widest, labels[i].Width);
            var hidden = !Horizontal && Hidden(i, labels.Length);
            if (hidden || (Crowding == UiChartCrowding.Ends && !Horizontal))
            {
                continue;
            }

            var at = labels[i].Unit * Width;
            if (Crowding == UiChartCrowding.Turned && !Horizontal)
            {
                reach.Left = Math.Max(reach.Left, -(at + (Diagonal * (LabelOffset - labels[i].Width))));
                reach.Right = Math.Max(reach.Right, at + (Diagonal * (LabelOffset + LabelHeight)) - Width);
                continue;
            }

            reach.Left = Math.Max(reach.Left, (labels[i].Width / 2) - at);
            reach.Right = Math.Max(reach.Right, at + (labels[i].Width / 2) - Width);
        }

        reach.Bottom = Math.Max(reach.Bottom, Crowding == UiChartCrowding.Turned && !Horizontal
            ? Diagonal * (widest + LabelOffset + LabelHeight)
            : LabelOffset + LabelHeight);
    }

    // Labels beside the plot: one em off the axis, centred on their tick's height.
    private void ReachAlongSide(ref UiChartReach reach, ReadOnlySpan<UiChartLabel> labels, bool fromTop, bool right)
    {
        foreach (var label in labels)
        {
            var at = fromTop ? label.Unit * Height : Height - (label.Unit * Height);
            reach.Top = Math.Max(reach.Top, (LabelHeight / 2) - at);
            reach.Bottom = Math.Max(reach.Bottom, at + (LabelHeight / 2) - Height);
            if (right)
            {
                reach.Right = Math.Max(reach.Right, LabelOffset + label.Width);
            }
            else
            {
                reach.Left = Math.Max(reach.Left, LabelOffset + label.Width);
            }
        }
    }

    private void ReachOfPoints(ref UiChartReach reach)
    {
        foreach (var part in _parts.Series)
        {
            if (part is not UiChartPoint point)
            {
                continue;
            }

            var radius = point.R ?? 4;
            for (var row = 0; row < Units.Length; row++)
            {
                var value = Values.Fraction(_parts.Read(point.Field, row));
                var (x, y) = Horizontal ? (value * Width, Units[row] * Height) : (Units[row] * Width, Height - (value * Height));
                reach.Left = Math.Max(reach.Left, radius - x);
                reach.Right = Math.Max(reach.Right, x + radius - Width);
                reach.Top = Math.Max(reach.Top, radius - y);
                reach.Bottom = Math.Max(reach.Bottom, y + radius - Height);
            }
        }
    }

    // Flux's gutter: one to four numbers, as CSS reads a padding shorthand. 8 all round when unset.
    internal static double[] Gutter(string? gutter)
    {
        if (string.IsNullOrWhiteSpace(gutter))
        {
            return [8, 8, 8, 8];
        }

        var parts = gutter.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var values = Array.ConvertAll(parts, part => double.Parse(part, CultureInfo.InvariantCulture));
        return values.Length switch
        {
            1 => [values[0], values[0], values[0], values[0]],
            2 => [values[0], values[1], values[0], values[1]],
            3 => [values[0], values[1], values[2], values[1]],
            _ => [values[0], values[1], values[2], values[3]],
        };
    }
}
