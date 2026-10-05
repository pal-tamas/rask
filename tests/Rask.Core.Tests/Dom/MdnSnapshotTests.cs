using System.Text.Json;
using Rask.Core.Components;

namespace Rask.Core.Tests.Dom;

/// <summary>
///     MDN is the source of truth for Rask's elements: <c>scripts/mdn/refresh.sh</c> writes
///     <c>src/Rask.Core/Dom/mdn.snapshot.json</c> from the latest MDN data. These guards keep a refresh honest.
/// </summary>
public class MdnSnapshotTests
{
    private static readonly JsonElement Snapshot = Load();

    [Fact]
    public void Every_tag_Rask_renders_is_an_element_MDN_ships()
    {
        var mdn = Snapshot.GetProperty("elements").EnumerateArray()
            .Select(e => $"{e.GetProperty("namespace").GetString()}:{e.GetProperty("tag").GetString()}")
            .ToHashSet(StringComparer.Ordinal);

        var missing = RaskTags().Where(tag => !mdn.Contains(tag)).ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void Every_element_MDN_ships_is_a_tag_Rask_renders()
    {
        // Read from the [Tag]s, which a type several tags share (HTMLElement, SVG's none) carries once per tag.
        var rask = typeof(Element).Assembly.GetTypes()
            .SelectMany(t => t.GetCustomAttributes(typeof(TagAttribute), false).Cast<TagAttribute>()
                .Select(a => (typeof(SVGElement).IsAssignableFrom(t) ? "svg:" : "html:") + a.Name))
            .ToHashSet(StringComparer.Ordinal);

        var missing = Snapshot.GetProperty("elements").EnumerateArray()
            .Select(e => $"{e.GetProperty("namespace").GetString()}:{e.GetProperty("tag").GetString()}")
            .Where(tag => !rask.Contains(tag))
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void Every_element_names_an_interface_the_snapshot_describes()
    {
        var interfaces = Snapshot.GetProperty("interfaces");

        var dangling = Snapshot.GetProperty("elements").EnumerateArray()
            .Select(e => e.GetProperty("interface").GetString()!)
            .Where(name => !interfaces.TryGetProperty(name, out _))
            .ToList();

        Assert.Empty(dangling);
    }

    [Fact]
    public void Tags_share_an_interface_the_way_the_DOM_shares_it()
    {
        var byTag = Snapshot.GetProperty("elements").EnumerateArray()
            .Where(e => e.GetProperty("namespace").GetString() == "html")
            .ToDictionary(e => e.GetProperty("tag").GetString()!, e => e.GetProperty("interface").GetString()!);

        var shared = new[] { byTag["em"], byTag["h1"], byTag["h6"], byTag["td"], byTag["th"], byTag["a"] };

        Assert.Equal(["HTMLElement", "HTMLHeadingElement", "HTMLHeadingElement", "HTMLTableCellElement", "HTMLTableCellElement", "HTMLAnchorElement"], shared);
    }

    private static IEnumerable<string> RaskTags()
    {
        foreach (var type in typeof(Element).Assembly.GetTypes())
        {
            if (type.IsAbstract || !typeof(Element).IsAssignableFrom(type))
                continue;
            var concrete = type.IsGenericTypeDefinition ? type.MakeGenericType(typeof(string)) : type;
            if (concrete.GetConstructor(Type.EmptyTypes) is null)
                continue;
            if (((Element)Activator.CreateInstance(concrete)!).TagNameInternal is not { } tag)
                continue;
            yield return (typeof(SVGElement).IsAssignableFrom(type) ? "svg:" : "html:") + tag;
        }
    }

    [Fact]
    public void The_ARIA_spec_is_read_at_a_pinned_commit_with_a_pinned_parser()
    {
        var sources = Snapshot.GetProperty("sources");

        var aria = sources.GetProperty("w3c/aria").GetString();
        var parser = sources.GetProperty("parse5").GetString();

        Assert.Matches("^[0-9a-f]{40}$", aria);
        Assert.Matches(@"^\d+\.\d+\.\d+$", parser);
    }

    [Fact]
    public void Every_ARIA_attribute_the_spec_has_not_deprecated_is_a_typed_property_on_Element()
    {
        var typed = typeof(Element).GetProperties()
            .Select(p => p.GetCustomAttributes(typeof(System.CodeDom.Compiler.GeneratedCodeAttribute), false).Length > 0 ? p.Name : null)
            .OfType<string>()
            .ToList();

        var expected = Snapshot.GetProperty("aria").GetProperty("attributes").EnumerateArray()
            .Count(a => !a.TryGetProperty("deprecated", out var d) || !d.GetBoolean());

        Assert.Equal(expected, typed.Count(name => name.StartsWith("Aria", StringComparison.Ordinal)));
    }

    private static JsonElement Load()
    {
        for (var dir = AppContext.BaseDirectory; dir is not null; dir = Path.GetDirectoryName(dir))
        {
            if (File.Exists(Path.Combine(dir, "Rask.slnx")))
                return JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "src", "Rask.Core", "Dom", "mdn.snapshot.json"))).RootElement;
        }
        throw new InvalidOperationException("Rask.slnx not found above the test output.");
    }
}
