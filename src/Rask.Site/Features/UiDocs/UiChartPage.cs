using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's chart and its parts as a C# component: each example with its own code, and its reference.</summary>
[Route("chart")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiChartPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Chart.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Chart)
            .Lead("A chart of rows, assembled from parts as Flux's is, and drawn as SVG in C# with no chart library. "
                  + "Move the pointer over one: the cursor, the tooltip, the summary and a pie's slice follow it in the "
                  + "browser, with no round trip.")
            .Api(UiApi.Chart)[
            UiDocsExample.Key("ui-chart-line").Heading("Line").File("ChartExample.cs").Demo(ChartExample).Testid("ui-chart-line")
                .Blurb("A line, its axes, a cursor and a tooltip that reads the row under the pointer."),
            UiDocsExample.Key("ui-chart-points").Heading("Line with points").File("ChartPointsExample.cs").Demo(ChartPointsExample).Testid("ui-chart-points")
                .Blurb("A dot on every row, on a time axis, with the value axis written as a unit."),
            UiDocsExample.Key("ui-chart-area").Heading("Area").File("ChartAreaExample.cs").Demo(ChartAreaExample).Testid("ui-chart-area")
                .Blurb("The area under a straight line, with the value axis on the right in compact dollars."),
            UiDocsExample.Key("ui-chart-lines").Heading("Multiple lines").File("ChartMultipleLinesExample.cs").Demo(ChartMultipleLinesExample).Testid("ui-chart-lines")
                .Blurb("A line and points per series, a percent axis from 0 to 1, and a legend under the drawing."),
            UiDocsExample.Key("ui-chart-bar").Heading("Bar").File("ChartBarExample.cs").Demo(ChartBarExample).Testid("ui-chart-bar")
                .Blurb("A bar per row, about eight ticks along the bottom, and a cursor that shades the row's band."),
            UiDocsExample.Key("ui-chart-horizontal").Heading("Horizontal").File("ChartHorizontalExample.cs").Demo(ChartHorizontalExample).Testid("ui-chart-horizontal")
                .Blurb("Horizontal() puts the rows down the Y axis: bars on their side."),
            UiDocsExample.Key("ui-chart-grouped").Heading("Grouped bars").File("ChartGroupedBarsExample.cs").Demo(ChartGroupedBarsExample).Testid("ui-chart-grouped")
                .Blurb("Bars side by side in each row's slot."),
            UiDocsExample.Key("ui-chart-stacked").Heading("Stacked bars").File("ChartStackedBarsExample.cs").Demo(ChartStackedBarsExample).Testid("ui-chart-stacked")
                .Blurb("Bars on top of one another, the last one rounded."),
            UiDocsExample.Key("ui-chart-pie").Heading("Pie and donut").File("ChartPieExample.cs").Demo(ChartPieExample).Testid("ui-chart-pie")
                .Blurb("A pie, a donut, and a donut with a figure in its hole. The slice under the pointer is marked."),
            UiDocsExample.Key("ui-chart-slices").Heading("Slice colours").File("ChartSliceColorsExample.cs").Demo(ChartSliceColorsExample).Testid("ui-chart-slices")
                .Blurb("ColorField lets each row choose its hue, with a legend beside the donut."),
            UiDocsExample.Key("ui-chart-summary").Heading("Live summary").File("ChartSummaryExample.cs").Demo(ChartSummaryExample).Testid("ui-chart-summary")
                .Blurb("Figures that read the row under the pointer, and the latest row when it leaves."),
            UiDocsExample.Key("ui-chart-sparkline").Heading("Sparkline and dashboard stat").File("ChartSparklineExample.cs").Demo(ChartSparklineExample).Testid("ui-chart-sparkline")
                .Blurb("Bare numbers need no field; Gutter(\"0\") draws to the edges."),
            UiChartBehaviour
        ];
}
