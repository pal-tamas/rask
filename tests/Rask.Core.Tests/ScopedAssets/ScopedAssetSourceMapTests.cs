using System.Text;
using System.Text.Json;
using Rask.Core.ScopedAssets;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.ScopedAssets;

// #1073: a Debug build's scoped TypeScript is emitted with its source map inline. The registry keeps the map, serves the
// bundle's as an index map, and keeps every mapped line where its map says it is — so a breakpoint in the .ts binds.
[Collection("ScopedAssets")]
public sealed class ScopedAssetSourceMapTests
{
    private const string Map = """{"version":3,"file":"Counter.js","sourceRoot":"file:///app/","sources":["Features/Counter.ts"],"names":[],"mappings":"AAAA"}""";

    public ScopedAssetSourceMapTests() => ScopedAssetRegistry.InvalidateAll();

    private static string WithInlineMap(string js, string map = Map) =>
        js + "//# sourceMappingURL=data:application/json;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(map));

    [Fact]
    public void The_bundle_names_its_map_and_the_map_is_an_index_offset_past_the_wrapper()
    {
        const string js = "const label = \"count\";\n\n\nexport function increment(value) {\n    return value + 1;\n}\n";
        ScopedAssetRegistry.RegisterJs(typeof(Counter), WithInlineMap(js));

        var hash = ScopedAssetRegistry.GetBundleHash(AssetKind.Js);
        var bundle = Encoding.UTF8.GetString(ScopedAssetRegistry.GetByHash(hash, AssetKind.Js)!.Value.Utf8.Span);

        Assert.DoesNotContain("data:application/json", bundle, StringComparison.Ordinal);
        Assert.EndsWith($"//# sourceMappingURL={hash}.js.map\n", bundle, StringComparison.Ordinal);

        using var index = JsonDocument.Parse(ScopedAssetRegistry.GetSourceMap(hash)!.Value.Utf8);
        Assert.Equal(3, index.RootElement.GetProperty("version").GetInt32());
        var section = index.RootElement.GetProperty("sections").EnumerateArray().Single();
        var offset = section.GetProperty("offset").GetProperty("line").GetInt32();
        Assert.Equal("Features/Counter.ts", section.GetProperty("map").GetProperty("sources")[0].GetString());

        // Every source line sits at offset + its own index, at the column it had — including the blank lines the
        // export strip would otherwise have swallowed, and the `export ` it replaces.
        var bundleLines = bundle.Split('\n');
        var sourceLines = js.Split('\n');
        for (var i = 0; i < sourceLines.Length - 1; i++)
        {
            var expected = sourceLines[i].StartsWith("export ", StringComparison.Ordinal)
                ? new string(' ', "export ".Length) + sourceLines[i]["export ".Length..]
                : sourceLines[i];
            Assert.Equal(expected, bundleLines[offset + i]);
        }
    }

    [Fact]
    public void Sections_follow_each_mapped_entry_through_the_concatenation()
    {
        ScopedAssetRegistry.RegisterJs(typeof(Counter), WithInlineMap("export function a() {\n    return 1;\n}\n"));
        ScopedAssetRegistry.RegisterJs(typeof(Toggle), WithInlineMap("export function b() {\n    return 2;\n}\n", Map.Replace("Counter", "Toggle", StringComparison.Ordinal)));

        var hash = ScopedAssetRegistry.GetBundleHash(AssetKind.Js);
        var lines = Encoding.UTF8.GetString(ScopedAssetRegistry.GetByHash(hash, AssetKind.Js)!.Value.Utf8.Span).Split('\n');
        using var index = JsonDocument.Parse(ScopedAssetRegistry.GetSourceMap(hash)!.Value.Utf8);

        foreach (var section in index.RootElement.GetProperty("sections").EnumerateArray())
        {
            var offset = section.GetProperty("offset").GetProperty("line").GetInt32();
            var name = section.GetProperty("map").GetProperty("sources")[0].GetString()!.Contains("Toggle", StringComparison.Ordinal) ? "b" : "a";
            Assert.Equal($"       function {name}() {{", lines[offset]);
        }
    }

    [Fact]
    public void A_release_emit_has_no_map_and_its_exports_are_stripped_as_before()
    {
        ScopedAssetRegistry.RegisterJs(typeof(Counter), "export function increment(value) {\n    return value + 1;\n}\n");

        var hash = ScopedAssetRegistry.GetBundleHash(AssetKind.Js);
        var bundle = Encoding.UTF8.GetString(ScopedAssetRegistry.GetByHash(hash, AssetKind.Js)!.Value.Utf8.Span);

        Assert.Null(ScopedAssetRegistry.GetSourceMap(hash));
        Assert.DoesNotContain("sourceMappingURL", bundle, StringComparison.Ordinal);
        Assert.Contains("\nfunction increment(value) {", bundle, StringComparison.Ordinal);
    }

    [Fact]
    public void A_map_for_another_hash_is_not_served()
    {
        ScopedAssetRegistry.RegisterJs(typeof(Counter), WithInlineMap("export function a() {}\n"));

        Assert.Null(ScopedAssetRegistry.GetSourceMap("000000000000"));
    }

    [Fact]
    public void A_comment_that_does_not_decode_is_left_as_source()
    {
        var source = "export function a() {}\n//# sourceMappingURL=data:application/json;base64,%%%not-base64%%%";

        Assert.Null(ScopedAssetRegistry.ExtractInlineSourceMap(ref source));
        Assert.EndsWith("%%%not-base64%%%", source, StringComparison.Ordinal);
    }

    private sealed class Counter : Component
    {
        protected override Component? Render() => Div;
    }

    private sealed class Toggle : Component
    {
        protected override Component? Render() => Div;
    }
}
