using System.Globalization;
using System.Text.RegularExpressions;
using Rask.Example.Shared.Demos;
using Rask.Example.Shared.Tests.Infrastructure;
using static Rask.Example.Shared.Demos.Generated;

namespace Rask.Example.Shared.Tests.Demos;

// Sparkline is a pure, stateless SVG line chart. These tests render it directly and
// assert on the emitted markup — no JavaScript, no canvas, no live transport involved.
public sealed class SparklineTests
{
    [Fact]
    public void Render_Empty_RendersNoDataFrame()
    {
        var html = Render();
        Assert.Contains("<svg", html);
        Assert.Contains("No data", html);
        Assert.DoesNotContain("<polyline", html);
    }

    [Fact]
    public void Render_SinglePoint_RendersMarkerButNoLine()
    {
        var html = Render(42d);
        Assert.Contains("<svg", html);
        // A single sample has no segment to draw — just the last-point marker.
        Assert.DoesNotContain("<polyline", html);
        Assert.Contains("<circle", html);
    }

    [Fact]
    public void Render_MultiplePoints_EmitsOneCoordinatePerPoint()
    {
        var html = Render(100d, 120d, 110d, 140d);
        var points = PolylinePoints(html);
        Assert.Equal(4, points.Length);
    }

    [Fact]
    public void Render_EmitsMinAndMaxLabels()
    {
        var html = Render(100d, 200d, 150d);
        Assert.Contains("$200.00", html); // max
        Assert.Contains("$100.00", html); // min
    }

    [Fact]
    public void Render_LastPoint_CarriesTitleTooltip()
    {
        var html = Render(100d, 120d, 137.5d);
        // Native SVG <title> on the marker — a no-JS hover tooltip with the current value.
        Assert.Contains("<title>$137.50</title>", html);
    }

    [Fact]
    public void Render_IncreasingValues_PutLastPointNearTop()
    {
        var html = Render(10d, 20d, 30d, 40d, 50d);
        var points = PolylinePoints(html);

        // SVG y grows downward, so the largest (last) value must have the smallest y.
        var firstY = points[0].Y;
        var lastY = points[^1].Y;
        Assert.True(lastY < firstY, $"expected last point ({lastY}) above first ({firstY})");
    }

    private static string Render(params double[] values) =>
        new LiveHost(() => Sparkline(Values: values), LiveHost.Services()).RenderAsLiveRoot();

    private static (double X, double Y)[] PolylinePoints(string html)
    {
        var m = Regex.Match(html, @"<polyline[^>]*\bpoints=""([^""]+)""");
        Assert.True(m.Success, "no <polyline points=\"…\"> found");
        return m.Groups[1].Value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair =>
            {
                var xy = pair.Split(',');
                return (
                    double.Parse(xy[0], CultureInfo.InvariantCulture),
                    double.Parse(xy[1], CultureInfo.InvariantCulture));
            })
            .ToArray();
    }
}
