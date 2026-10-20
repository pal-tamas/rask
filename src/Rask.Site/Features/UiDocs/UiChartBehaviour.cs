namespace Rask.Site.Features.UiDocs;

/// <summary>
///     How a chart behaves: where it is drawn, the rows it takes, its axes and size, how it writes numbers and dates,
///     what follows the pointer, and how its labels are measured.
/// </summary>
internal sealed partial class UiChartBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiDocsBehaviour[
            Drawn(),
            Data(),
            Axes(),
            Size(),
            Format(),
            Pointer(),
            Labels()
        ];

    private static UiDocsTopic Drawn() =>
        UiDocsTopic.Key("drawn").Heading("Drawn in C#").Paragraphs([
            "No chart library: the scales, the ticks and every path are computed during the render, to the numbers "
            + "Flux's own layout arrives at, for the box the chart has in the browser (`scripts/flux/parity-chart.mjs` "
            + "compares the two drawings' geometry, and with `--pointer` what each does under a pointer). What Flux's "
            + "own script does after that, measuring and following the pointer, is the runtime's plot hook, by "
            + "attribute.",
        ]);

    private static UiDocsTopic Data() =>
        UiDocsTopic.Key("data").Heading("The rows").Paragraphs([
            "Flux's chart takes an array of rows and its parts name a column with a string. `Ui.Chart.Value(rows)` "
            + "takes the typed list, and each part takes a selector whose lambda states the row type once, "
            + "`.Field((Visit v) => v.Visitors)`, because a part is built before the chart it goes into.",
            "A number of any numeric type plots without a cast; a `DateTime`, `DateOnly` or `DateTimeOffset` on the "
            + "index axis makes it a time axis; anything else is a name. A chart of bare numbers needs no field at "
            + "all: `Ui.Chart.Value([15, 18, 16])[Ui.ChartSvg.Gutter(\"0\")[Ui.ChartLine]]`.",
        ]);

    private static UiDocsTopic Axes() =>
        UiDocsTopic.Key("axes").Heading("Axes").Paragraphs([
            "The value axis starts at zero (or the lowest value below it) and steps by a round quarter of its span; "
            + "`TickCount`, `TickStart`, `TickEnd`, `TickValues`, `TickPrefix`, `TickSuffix` and "
            + "`Position(Ui.Position.Right)` are Flux's.",
            "A time axis ticks at the pace of its rows and writes itself for its reach (`9:17 AM`, `9 AM`, `Tue 9 AM`, "
            + "`Mar 10`, `Mar`, `2026`). Labels that would touch give way as Flux's do: dates drop every other one, "
            + "names turn 45°.",
        ]);

    private static UiDocsTopic Size() =>
        UiDocsTopic.Key("size").Heading("Drawn for its box").Paragraphs([
            "As Flux's, the chart fills the box its class gives it (`aspect-3/1`, `h-64`) and is drawn for that box: "
            + "the browser measures the drawing (the runtime's `data-rask-measure` hook) and the chart is drawn again "
            + "in its units, when it first appears, 100ms after its box stops changing (a window resized, a phone "
            + "turned), and when a hidden chart is shown at a new size; a hidden chart keeps its drawing. Its 12px "
            + "labels are 12px at every width, and the ticks that fit are worked out again.",
            "The first render happens before anything is measured, so it draws nothing: an empty box of the size its "
            + "class gives it, as Flux's own first paint is, and the drawing arrives with the measurement. Every chart "
            + "on a page is measured in the same browser task, and the events of one task are answered with one "
            + "render: a Server page of sixty charts is one frame out and one patch back; in WebAssembly it never "
            + "leaves the browser. A page prerendered for a WebAssembly app shows the empty boxes until the runtime "
            + "has started.",
        ]);

    private static UiDocsTopic Format() =>
        UiDocsTopic.Key("format").Heading("How numbers and dates are written").Paragraphs([
            "Flux's `:format` is the options of `Intl.NumberFormat` and `Intl.DateTimeFormat`; `UiChartFormat` carries "
            + "them under Intl's names, written with .NET formatting in the reader's culture: "
            + "`new() { Style = Ui.ChartFormatStyle.Currency, Currency = \"USD\" }`, "
            + "`new() { Notation = Ui.ChartFormatNotation.Compact, MaximumFractionDigits = 1 }`, "
            + "`new() { Month = Ui.ChartFormatPart.Short, Day = Ui.ChartFormatPart.Numeric }`.",
            "Carried: `style` (decimal, currency, percent, unit), `currency`, `unit`, `notation` (standard, compact, "
            + "scientific), `minimumFractionDigits`, `maximumFractionDigits`, `useGrouping`, `dateStyle`, `timeStyle`, "
            + "`weekday`, `year`, `month`, `day`, `hour`, `minute`, `second`, `hour12`. A date's parts are written in "
            + "the order English writes them.",
            "Not carried: `compactDisplay: long`, `currencyDisplay`, `currencySign`, `signDisplay`, `unitDisplay`, "
            + "significant-digit and integer-digit limits, `roundingMode`, `timeZone`, `timeZoneName`, `era`, "
            + "`fractionalSecondDigits`, `dayPeriod`, `hourCycle`, `calendar`, `numberingSystem`.",
        ]);

    private static UiDocsTopic Pointer() =>
        UiDocsTopic.Key("pointer").Heading("Under the pointer").Paragraphs([
            "Everything Flux's chart does under the pointer is done in the browser by the runtime's plot hook: no "
            + "handler, no round trip per move. The row nearest the pointer along the index axis is the active one "
            + "while the pointer is inside the plot. One cursor moves to it (a dashed line; `Ui.ChartCursorType.Area` "
            + "covers the row's band), and one tooltip moves beside it, 15px from the row and from the pointer, flipped "
            + "to the other side where it would pass the drawing's right or bottom edge. Its heading and values, and "
            + "every `Ui.ChartSummaryValue`, read that row; a summary goes back to the latest row when the pointer "
            + "leaves. The row's `Ui.ChartPoint`s carry `data-active`.",
            "On a pie the slice under the pointer carries `data-active` and the others `data-inactive` "
            + "(`.Class(\"transition-opacity data-inactive:opacity-40\")`), the tooltip follows the pointer, and a "
            + "`Ui.ChartTooltipIndicator` takes the slice's colour.",
            "Each part is rendered once with what it reads for every row (`data-rask-plot-text`), so fifty rows cost "
            + "fifty short lines, not fifty tooltips. `node scripts/flux/parity-chart.mjs --pointer` walks a pointer "
            + "over Flux's page and the kit's and compares.",
        ]);

    private static UiDocsTopic Labels() =>
        UiDocsTopic.Key("labels").Heading("Labels are measured in Inter").Paragraphs([
            "Flux sizes a chart's gutters by measuring its tick labels in the browser. The kit carries Inter's advance "
            + "widths and kerning (`UiChartInter`, generated by `scripts/flux/inter-metrics.mjs`) and adds them up as "
            + "Chromium does. In another face the labels still fit; the gutters are those Inter would have needed.",
        ]);
}
