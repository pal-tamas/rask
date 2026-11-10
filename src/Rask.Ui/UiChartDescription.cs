using System.Globalization;
using System.Net;
using System.Text;

namespace Rask;

/// <summary>
///     What a chart says to a screen reader, which cannot see the drawing: a name for the <c>role="img"</c> SVG — the kind
///     of chart, its series and the reach of its rows and values — and its rows as a table no one sees, which the SVG
///     names as its description (<c>aria-describedby</c>).
/// </summary>
/// <remarks>
///     <para>
///         Not Flux's, whose chart says nothing to a screen reader: an addition the owner approved (2026-11-10). It draws
///         nothing: the table sits in an <c>sr-only</c> box, since a table given <c>sr-only</c> itself grows to fit its
///         cells — a table is never narrower than its content. Its words are the <c>Chart…</c> keys of
///         <see cref="RaskString" />.
///     </para>
///     <para>
///         A series is named by what the page already named it with: the tooltip value or the legend entry that reads
///         the same numbers, row for row — a field is a selector, so two parts reading one member are told apart by what
///         they read, not by the lambda. A part with no field names a series only on a chart of bare numbers, never by
///         its place: a legend's entries need not run in the series' order. The same values drawn twice (an area under
///         its line) are one column. A series nothing names is "Value", or "Series 2" beside others. A value with no
///         format of its own is written as the value axis writes it.
///     </para>
/// </remarks>
internal sealed class UiChartDescription
{
    private readonly List<Column> _values = [];

    // How the value axis writes its numbers: what a series is written with where what names it says nothing of that.
    private readonly UiChartFormat? _valueFormat;

    private UiChartDescription(UiChartParts parts, UiChartData data, bool horizontal, IReadOnlyList<Component> named)
    {
        Data = data;
        Kind = KindOf(parts);
        Slices = parts.Pie is not null;
        Index = IndexOf(parts, data, horizontal, named);
        _valueFormat = parts.Pie is null ? parts.ValueAxis(horizontal)?.Formatting : null;
        var labelled = named.Where(part => !string.IsNullOrWhiteSpace(LabelOf(part))).ToList();
        foreach (var field in Fields(parts))
        {
            Add(field, data, labelled);
        }

        Unnamed();
    }

    private UiChartData Data { get; }

    private string Kind { get; }

    // A pie's rows are slices; any other chart's are data points.
    private bool Slices { get; }

    private Column Index { get; }

    /// <summary>The SVG's accessible name: <c>"Line chart: Visitors. 16 data points, 9/22/2026 to 10/7/2026. Values from 115 to 300."</c></summary>
    internal string Label { get; private set; } = "";

    /// <summary>The rows as a table in an <c>sr-only</c> box, under <paramref name="id" />: a heading per column, a row per row.</summary>
    internal string Table(string id)
    {
        var table = new StringBuilder(64 + (Data.Count * 16 * (_values.Count + 1)))
            .Append("<div class=\"sr-only\" data-ui-chart-rows><table id=\"").Append(id).Append("\"><thead><tr><th scope=\"col\">").Append(WebUtility.HtmlEncode(Index.Name)).Append("</th>");
        foreach (var column in _values)
        {
            table.Append("<th scope=\"col\">").Append(WebUtility.HtmlEncode(column.Name)).Append("</th>");
        }

        table.Append("</tr></thead><tbody>");
        for (var row = 0; row < Data.Count; row++)
        {
            table.Append("<tr><th scope=\"row\">").Append(WebUtility.HtmlEncode(Index.Write(row))).Append("</th>");
            foreach (var column in _values)
            {
                table.Append("<td>").Append(WebUtility.HtmlEncode(column.Write(row))).Append("</td>");
            }

            table.Append("</tr>");
        }

        return table.Append("</tbody></table></div>").ToString();
    }

    /// <summary>What the chart drawn from <paramref name="parts" /> says to a screen reader.</summary>
    internal static UiChartDescription Of(UiChartParts parts, UiChartData data, bool horizontal, IReadOnlyList<Component> named)
    {
        var description = new UiChartDescription(parts, data, horizontal, named);
        description.Label = description.Spoken();
        return description;
    }

    // The kind, the series, and — when there are rows — how many, from which to which, and the least and most value.
    private string Spoken()
    {
        var name = new StringBuilder(Kind);
        if (_values.Count > 0)
        {
            name.Append(": ").AppendJoin(", ", _values.Select(column => column.Name));
        }

        name.Append(". ");
        if (Data.Count == 0)
        {
            return name.Append(RaskStrings.Get(RaskString.ChartNoData, "No data.")).ToString();
        }

        name.Append(Reached());
        if (Reach() is var (low, high))
        {
            name.Append(' ').Append(RaskStrings.Get(RaskString.ChartValueRange, "Values from {0} to {1}.", low, high));
        }

        return name.ToString();
    }

    // How many rows, and the first and the last by name: a pie's are slices.
    private string Reached()
    {
        var (first, last) = (Index.Write(0), Index.Write(Data.Count - 1));
        if (Data.Count == 1)
        {
            return Slices
                ? RaskStrings.Get(RaskString.ChartSlice, "1 slice, {0}.", first)
                : RaskStrings.Get(RaskString.ChartDataPoint, "1 data point, {0}.", first);
        }

        return Slices
            ? RaskStrings.Get(RaskString.ChartSlices, "{0} slices, {1} to {2}.", Data.Count, first, last)
            : RaskStrings.Get(RaskString.ChartDataPoints, "{0} data points, {1} to {2}.", Data.Count, first, last);
    }

    // The least and the most value of every series, each written as its own column writes it.
    private (string Low, string High)? Reach()
    {
        (double Value, string Written)? low = null, high = null;
        foreach (var column in _values)
        {
            for (var row = 0; row < Data.Count; row++)
            {
                var value = column.Numbers[row];
                if (double.IsNaN(value) || double.IsInfinity(value))
                {
                    continue;
                }

                if (low is null || value < low.Value.Value)
                {
                    low = (value, column.Write(row));
                }

                if (high is null || value > high.Value.Value)
                {
                    high = (value, column.Write(row));
                }
            }
        }

        return low is { } l && high is { } h ? (l.Written, h.Written) : null;
    }

    // A series, named by the labelled part that reads the same numbers — or, with no rows to tell them apart by, the
    // labelled part in its place — and written as that part writes it. One that repeats a column is left out.
    private void Add(UiChartField? field, UiChartData data, List<Component> labelled)
    {
        var numbers = NumbersOf(field, data);
        if (data.Count > 0 && _values.Exists(column => SameNumbers(column.Numbers, numbers)))
        {
            return; // an area under its line, a point on it: the same values, drawn twice
        }

        var match = data.Count > 0
            ? labelled.Find(part => Reads(part) is var reads && (reads is null || reads.Kind == UiChartFieldKind.Number) && SameNumbers(NumbersOf(reads, data), numbers))
            : labelled.ElementAtOrDefault(_values.Count);
        if (match is null)
        {
            var axis = _valueFormat;
            _values.Add(new Column("", numbers, row => UiChartWriting.Value(field, axis, data, row)));
            return;
        }

        var (reads, format, prefix, suffix) = match switch
        {
            UiChartTooltipValue value => (value.Field, value.Format, value.Prefix, value.Suffix),
            UiChartLegend legend => (legend.Field, legend.Format, null, null),
            _ => (field, null, null, null),
        };
        var written = format ?? _valueFormat;
        _values.Add(new Column(LabelOf(match)!, numbers, row => prefix + UiChartWriting.Value(reads, written, data, row) + suffix));
    }

    // A series nothing names: "Value" alone, numbered by its column beside others.
    private void Unnamed()
    {
        for (var at = 0; at < _values.Count; at++)
        {
            if (_values[at].Name.Length == 0)
            {
                _values[at] = _values[at] with
                {
                    Name = _values.Count == 1
                        ? RaskStrings.Get(RaskString.ChartColumnValue, "Value")
                        : RaskStrings.Get(RaskString.ChartColumnSeries, "Series {0}", at + 1),
                };
            }
        }
    }

    // What a part that names a series calls it, and what it reads.
    private static string? LabelOf(Component part) => part switch
    {
        UiChartTooltipValue value => value.Label,
        UiChartLegend legend => legend.Label,
        _ => null,
    };

    private static UiChartField? Reads(Component part) => part switch
    {
        UiChartTooltipValue value => value.Field,
        UiChartLegend legend => legend.Field,
        _ => null,
    };

    private static double[] NumbersOf(UiChartField? field, UiChartData data)
    {
        var numbers = new double[data.Count];
        for (var row = 0; row < numbers.Length; row++)
        {
            numbers[row] = field is null ? data.Number(row) : field.Number(data, row);
        }

        return numbers;
    }

    private static bool SameNumbers(double[] one, double[] other) => one.AsSpan().SequenceEqual(other);

    // What the series of the drawing read, in the order they are drawn: a group's or a stack's bars one by one, a pie's slices.
    private static IEnumerable<UiChartField?> Fields(UiChartParts parts)
    {
        if (parts.Pie is { } pie)
        {
            yield return pie.Field;
            yield break;
        }

        foreach (var part in parts.Series)
        {
            switch (part)
            {
                case UiChartGroup or UiChartStack:
                    foreach (var bar in part.Bars())
                    {
                        yield return bar.Field;
                    }

                    break;
                case IUiChartField single:
                    yield return single.Field;
                    break;
                default:
                    break;
            }
        }
    }

    // What names each row: the tooltip's heading, the pie's label or the index axis — or its place, counted from one.
    private static Column IndexOf(UiChartParts parts, UiChartData data, bool horizontal, IReadOnlyList<Component> named)
    {
        var (field, format) = IndexField(parts, horizontal, named);
        if (field is null)
        {
            return new Column(RaskStrings.Get(RaskString.ChartColumnRow, "Row"), [], row => (row + 1).ToString(CultureInfo.CurrentCulture));
        }

        var name = field.Kind == UiChartFieldKind.Time
            ? RaskStrings.Get(RaskString.ChartColumnDate, "Date")
            : RaskStrings.Get(RaskString.ChartColumnLabel, "Label");
        return new Column(name, [], row => UiChartWriting.Value(field, format, data, row));
    }

    private static (UiChartField? Field, UiChartFormat? Format) IndexField(UiChartParts parts, bool horizontal, IReadOnlyList<Component> named)
    {
        if (named.OfType<UiChartTooltipHeading>().FirstOrDefault(heading => heading.Field is not null) is { } heading)
        {
            return (heading.Field, heading.Format);
        }

        if (parts.Pie?.LabelField is { } label)
        {
            return (label, null);
        }

        return parts.IndexAxis(horizontal) is { Field: { } axis } index ? (axis, index.Formatting) : (null, null);
    }

    private static string KindOf(UiChartParts parts)
    {
        if (parts.Pie is { } pie)
        {
            return string.IsNullOrWhiteSpace(pie.InnerRadius)
                ? RaskStrings.Get(RaskString.ChartPie, "Pie chart")
                : RaskStrings.Get(RaskString.ChartDonut, "Donut chart");
        }

        return Called(Drawn(parts));
    }

    // What the series draw. An area is drawn with its line, and points sit on a line: the chart is what carries the
    // others; bars beside anything else make a chart of no one kind.
    private static RaskString Drawn(UiChartParts parts)
    {
        var kinds = parts.Series.Select(part => part switch
        {
            UiChartBar or UiChartGroup or UiChartStack => RaskString.ChartBar,
            UiChartArea => RaskString.ChartArea,
            UiChartLine => RaskString.ChartLine,
            _ => RaskString.ChartScatter,
        }).Distinct().ToList();

        return kinds switch
        {
            [var only] => only,
            _ when kinds.Contains(RaskString.ChartBar) => RaskString.Chart,
            _ when kinds.Contains(RaskString.ChartArea) => RaskString.ChartArea,
            _ when kinds.Contains(RaskString.ChartLine) => RaskString.ChartLine,
            _ => RaskString.Chart,
        };
    }

    private static string Called(RaskString kind) => kind switch
    {
        RaskString.ChartBar => RaskStrings.Get(RaskString.ChartBar, "Bar chart"),
        RaskString.ChartArea => RaskStrings.Get(RaskString.ChartArea, "Area chart"),
        RaskString.ChartLine => RaskStrings.Get(RaskString.ChartLine, "Line chart"),
        RaskString.ChartScatter => RaskStrings.Get(RaskString.ChartScatter, "Scatter chart"),
        _ => RaskStrings.Get(RaskString.Chart, "Chart"),
    };

    private sealed record Column(string Name, double[] Numbers, Func<int, string> Write);
}
