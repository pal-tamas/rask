using System.Text;
using System.Text.Encodings.Web;
using BenchmarkDotNet.Attributes;

namespace Rask.Benchmarks;

// Measures HtmlEncoder.Default.Encode + StringBuilder.Append over a realistic mix of
// attribute values (URLs with query strings, CSS strings, class lists, plain text,
// quoted strings). The encoder runs once per attribute value, every render — so a
// page with 200 elements × 5 attributes each runs Encode 1000 times per frame. The
// encoder itself is SIMD-vectorized on .NET 9+; this benchmark exists to track the
// cost so any future change (caching, bypassing for known-safe values) has a
// reference point.
[MemoryDiagnoser]
public class AttributeEncodingBenchmarks
{
    private string[] _values = null!;

    [GlobalSetup]
    public void Setup()
    {
        _values =
        [
            "/api/items/42",
            "/api/items/42?sort=name&dir=asc&page=2",
            "display:flex;gap:8px;align-items:center;padding:0 12px",
            "btn btn-primary btn-lg with-icon",
            "Simple plain text",
            "Text with \"quotes\" and <angle> brackets",
            "user@example.com",
            "row-42",
            "_blank",
            "noopener noreferrer",
            "lazy",
            "text/plain; charset=utf-8",
            "Item 42: a moderately long string that simulates a label or aria-label",
            "https://example.com/path/to/resource?with=query&more=params#fragment",
            "background-image: url('/img/hero.png'); background-size: cover;"
        ];
    }

    [Benchmark]
    public string EncodeAll()
    {
        // Mirror the per-render shape: append attribute values into a StringBuilder, one
        // Encode call per value. 15 values * the inner loop produces ~150 encoder calls
        // — within an order of magnitude of a moderate page's render.
        var sb = new StringBuilder(4096);
        for (var loop = 0; loop < 10; loop++)
        {
            for (var i = 0; i < _values.Length; i++)
            {
                sb.Append(' ').Append("data-v").Append(i).Append("=\"")
                    .Append(HtmlEncoder.Default.Encode(_values[i])).Append('"');
            }
        }

        return sb.ToString();
    }
}
