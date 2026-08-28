using System.Text;
using System.Text.Json;

namespace Rask.External.Tests;

/// <summary>A plot rendered by a sibling Chart.tsx.</summary>
public sealed partial class Chart : ReactComponent
{
    /// <summary>The points to plot.</summary>
    public required IReadOnlyList<Point> Series { get; set; }

    /// <summary>Heading shown above the plot.</summary>
    public string? Heading { get; set; }

    /// <summary>Runs when a point is clicked.</summary>
    public Action<int>? OnPointClick { get; set; }
}

/// <summary>One plotted point.</summary>
public sealed record Point(string Label, decimal Value);

/// <summary>A gauge implemented as a Lit custom element, with an explicit module.</summary>
public sealed partial class Gauge : LitComponent
{
    /// <summary>Points somewhere convention cannot reach.</summary>
    protected override string Module => "./widgets/gauge.ts";

    /// <summary>The needle position, 0..1.</summary>
    public double Value { get; set; }
}

/// <summary>A Lit gauge taking the module convention gives it.</summary>
public sealed partial class Dial : LitComponent
{
    /// <summary>The needle position, 0..1.</summary>
    public double Value { get; set; }
}

// Renders real components rather than asserting on generator output as text. What has to work is the
// whole seam: the base class is discovered, the partial is generated, the host element serializes
// with the diff-boundary marker, and the props JSON is exactly what the front end will be typed
// against.
public partial class ExternalRenderTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void An_island_renders_a_host_element_carrying_the_diff_boundary()
    {
        var html = Render(Chart.Series([new Point("Jan", 41200m)]));

        Assert.StartsWith("<rask-external ", html, StringComparison.Ordinal);
        Assert.Contains("data-rask-opaque", html, StringComparison.Ordinal);
        Assert.Contains("name=\"Chart\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_module_defaults_to_the_sibling_file()
    {
        var html = Render(Chart.Series([]));

        Assert.Contains("module=\"./Chart.tsx\"", html, StringComparison.Ordinal);
        Assert.Contains("runtime=\"react\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_lit_component_pairs_with_a_ts_file_rather_than_a_tsx_one()
    {
        // What naming the runtime in the BASE CLASS buys: a Lit component is ordinary TypeScript, so
        // before this the extension could not be inferred and every Lit component had to state its
        // module by hand. The type now says which it is, so convention can answer.
        var html = Render(Dial.Value(0.5));

        Assert.Contains("module=\"./Dial.ts\"", html, StringComparison.Ordinal);
        Assert.Contains("runtime=\"lit\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_overridden_module_wins_over_the_convention()
    {
        var html = Render(Gauge.Value(0.5));

        Assert.Contains("module=\"./widgets/gauge.ts\"", html, StringComparison.Ordinal);
        Assert.Contains("runtime=\"lit\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Props_are_camel_cased_json_matching_the_declared_shape()
    {
        var html = Render(Chart.Series([new Point("Jan", 41200m)]).Heading("Revenue"));

        using var props = JsonDocument.Parse(ReadProps(html));
        var root = props.RootElement;

        Assert.Equal("Revenue", root.GetProperty("heading").GetString());
        var series = root.GetProperty("series");
        Assert.Equal(1, series.GetArrayLength());
        Assert.Equal("Jan", series[0].GetProperty("label").GetString());
        Assert.Equal(41200m, series[0].GetProperty("value").GetDecimal());
    }

    [Fact]
    public void A_null_prop_is_written_as_null_not_omitted()
    {
        // Distinct from a null CALLBACK, which is omitted. A data prop that vanished from the object
        // would read in TypeScript as "never set" rather than "set to nothing", and the two differ.
        var html = Render(Chart.Series([]));

        using var props = JsonDocument.Parse(ReadProps(html));
        Assert.Equal(JsonValueKind.Null, props.RootElement.GetProperty("heading").ValueKind);
    }

    [Fact]
    public void A_wired_callback_travels_as_a_handler_reference()
    {
        var html = Render(Chart.Series([]).OnPointClick(_ => { }));

        using var props = JsonDocument.Parse(ReadProps(html));
        var handler = props.RootElement.GetProperty("onPointClick");

        // Never the delegate, and never a plain string: an object with the $h sentinel, which is what
        // the client runtime swaps for a real function before the adapter ever sees the props.
        Assert.Equal(JsonValueKind.Object, handler.ValueKind);
        Assert.False(string.IsNullOrEmpty(handler.GetProperty("$h").GetString()));
    }

    [Fact]
    public void An_unwired_callback_is_omitted_entirely()
    {
        // So the front end sees `undefined` and React's optional-prop handling applies. Writing null
        // would leave a key that still looks callable.
        var html = Render(Chart.Series([]));

        using var props = JsonDocument.Parse(ReadProps(html));
        Assert.False(props.RootElement.TryGetProperty("onPointClick", out _));
    }

    [Fact]
    public void An_island_renders_no_children_of_its_own()
    {
        // P0 islands are leaves: the subtree is created in the browser. The server emitting content
        // here would be content the morph then has to be told not to delete.
        var html = Render(Chart.Series([]));

        Assert.EndsWith("></rask-external>", html, StringComparison.Ordinal);
    }

    private static string Render(Component component)
    {
        var sb = new StringBuilder();
        HtmlSerializer.Serialize(component, sb);
        return sb.ToString();
    }

    /// <summary>The props attribute's decoded JSON.</summary>
    private static string ReadProps(string html)
    {
        const string marker = " props=\"";
        var start = html.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"no props attribute in: {html}");

        start += marker.Length;
        var end = html.IndexOf('"', start);
        Assert.True(end > start, $"unterminated props attribute in: {html}");

        // The serializer HTML-encodes the attribute value, so the quotes inside the JSON arrive as
        // &quot;. Decoding here rather than asserting on the encoded form keeps the tests about the
        // props rather than about HTML escaping.
        return System.Net.WebUtility.HtmlDecode(html[start..end]);
    }
}
