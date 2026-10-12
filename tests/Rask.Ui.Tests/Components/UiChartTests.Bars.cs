using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Rask.UiTests.Components;

/// <summary>
///     Bars, held to the paths Flux's live chart drew for the same rows in the same box.
/// </summary>
/// <remarks>
///     <c>UiChartTests.Bars.json</c> is what Flux's <c>&lt;ui-chart&gt;</c> drew: two loads of its docs page whose
///     random rows held a bar shorter than its corners (the kit was 1 to 3px out there), and rows handed to the
///     live element (<c>element.value = rows</c>) to ask about short, empty and negative bars of every kind.
/// </remarks>
public partial class UiChartTests
{
    private sealed record Bars(string Name, double A, double B, double C);

    public static TheoryData<string> BarFixtures() => [.. Fixtures().Select(fixture => fixture.GetProperty("name").GetString()!)];

    [Theory]
    [MemberData(nameof(BarFixtures))]
    public void Bars_are_drawn_as_Flux_draws_them_for_the_same_rows(string fixture)
    {
        var flux = Fixtures().Single(candidate => candidate.GetProperty("name").GetString() == fixture);
        var expected = flux.GetProperty("paths").EnumerateArray().Select(path => path.GetString()!).ToArray();

        var drawn = BarPaths(flux.GetProperty("kind").GetString()!, Rows(flux));

        Assert.Equal(expected.Length, drawn.Length);
        Assert.All(expected.Zip(drawn), pair => AssertSamePath(pair.First, pair.Second));
    }

    [Theory]
    [InlineData(8, 40, 14, 7)]
    [InlineData(8, 40, 16, 8)]
    [InlineData(8, 10, 100, 5)]
    [InlineData(4, 26, 3, 1.5)]
    [InlineData(0, 40, 14, 0)]
    public void A_corner_is_never_wider_than_half_the_bar_is_long_or_thick(double radius, double thickness, double length, double corner)
    {
        var upright = new System.Text.StringBuilder();
        var lying = new System.Text.StringBuilder();

        UiChartPaths.Bar(upright, 0, thickness, 100 - length, 100, radius);
        UiChartPaths.HorizontalBar(lying, 0, thickness, length, 0, radius);

        var arc = Regex.Escape(string.Create(CultureInfo.InvariantCulture, $"A{corner},{corner} 0 0 1 "));
        Assert.Equal(corner == 0 ? 0 : 2, Regex.Count(upright.ToString(), arc));
        Assert.Equal(corner == 0 ? 0 : 2, Regex.Count(lying.ToString(), arc));
    }

    [Fact]
    public void A_bar_lying_back_from_the_baseline_is_rounded_at_its_left_end()
    {
        var path = new System.Text.StringBuilder();

        UiChartPaths.HorizontalBar(path, y: 10, height: 20, value: 30, baseline: 100, radius: 4);

        Assert.Equal("M30,14A4,4 0 0 1 34,10H100V30H34A4,4 0 0 1 30,26V14Z", path.ToString());
    }

    private static JsonElement[] Fixtures() =>
        [.. JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot.FullPath, "tests", "Rask.Ui.Tests", "Components", "UiChartTests.Bars.json"))).RootElement.EnumerateArray()];

    private static Bars[] Rows(JsonElement fixture) =>
        [.. fixture.GetProperty("rows").EnumerateArray().Select(row => new Bars(
            row[0].GetString()!,
            row[1].GetDouble(),
            row.GetArrayLength() > 2 ? row[2].GetDouble() : 0,
            row.GetArrayLength() > 3 ? row[3].GetDouble() : 0))];

    // Each kind as the example on Flux's page writes it, in the box Flux measured for it.
    private static string[] BarPaths(string kind, Bars[] rows)
    {
        var html = InEnglish(() => MeasuredCharts.Html(kind switch
        {
            "bar" => Ui.Chart.Value(rows)[
                Ui.ChartSvg[
                    Ui.ChartBar.Field((Bars r) => r.A),
                    Ui.ChartAxis.X.Field((Bars r) => r.Name)[Ui.ChartAxisTick],
                    Ui.ChartAxis.Y[Ui.ChartAxisGrid, Ui.ChartAxisTick]]],
            "horizontal" => Ui.Chart.Horizontal().Value(rows)[
                Ui.ChartSvg[
                    Ui.ChartBar.Field((Bars r) => r.A).Radius("4 0").Width("70%"),
                    Ui.ChartAxis.Y.Field((Bars r) => r.Name)[Ui.ChartAxisTick, Ui.ChartAxisLine],
                    Ui.ChartAxis.X[Ui.ChartAxisGrid, Ui.ChartAxisTick]]],
            "grouped" => Ui.Chart.Value(rows)[
                Ui.ChartSvg[
                    Ui.ChartGroup[Ui.ChartBar.Field((Bars r) => r.A), Ui.ChartBar.Field((Bars r) => r.B), Ui.ChartBar.Field((Bars r) => r.C)],
                    Ui.ChartAxis.X.Field((Bars r) => r.Name)[Ui.ChartAxisTick, Ui.ChartAxisLine],
                    Ui.ChartAxis.Y[Ui.ChartAxisGrid, Ui.ChartAxisTick]]],
            _ => Ui.Chart.Value(rows)[
                Ui.ChartSvg[
                    Ui.ChartStack.Width("65%")[Ui.ChartBar.Field((Bars r) => r.A), Ui.ChartBar.Field((Bars r) => r.B), Ui.ChartBar.Field((Bars r) => r.C).Radius("4 0")],
                    Ui.ChartAxis.X.Field((Bars r) => r.Name)[Ui.ChartAxisTick, Ui.ChartAxisLine],
                    Ui.ChartAxis.Y[Ui.ChartAxisGrid, Ui.ChartAxisTick]]],
        }, (606, kind == "horizontal" ? 303 : 202)));
        return [.. BarPath().Matches(html).Select(match => match.Groups[1].Value)];
    }

    // The same commands in the same order, and every number within the parity tool's twentieth of a pixel.
    private static void AssertSamePath(string expected, string drawn)
    {
        Assert.Equal(Shape(expected), Shape(drawn));
        var theirs = Number().Matches(expected).Select(match => double.Parse(match.Value, CultureInfo.InvariantCulture)).ToArray();
        var ours = Number().Matches(drawn).Select(match => double.Parse(match.Value, CultureInfo.InvariantCulture)).ToArray();
        Assert.All(theirs.Zip(ours), pair => Assert.Equal(pair.First, pair.Second, 0.05));
    }

    private static string Shape(string path) => Spaces().Replace(Number().Replace(path, "#"), "");

    [GeneratedRegex("<path stroke=\"none\" fill=\"currentColor\"[^>]* d=\"([^\"]*)\"")]
    private static partial Regex BarPath();

    [GeneratedRegex(@"-?\d*\.?\d+(?:e[-+]?\d+)?", RegexOptions.IgnoreCase)]
    private static partial Regex Number();

    [GeneratedRegex(@"[\s,]+")]
    private static partial Regex Spaces();
}
