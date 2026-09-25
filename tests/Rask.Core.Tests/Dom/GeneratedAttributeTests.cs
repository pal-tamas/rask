using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Rask.Core.Tests.Dom;

// Every attribute property the build generated from MDN, on every HTML tag, rendered at once: the attributes
// must come out under MDN's names, in IDL order, after the globals. One test in place of a hundred per-tag
// files, because the tags are data now — a new one arrives with a snapshot refresh and is covered here.
public partial class GeneratedAttributeTests
{
    private static readonly JsonElement Snapshot = Load();

    public static TheoryData<string> Tags()
    {
        var data = new TheoryData<string>();
        foreach (var e in Snapshot.GetProperty("elements").EnumerateArray())
        {
            if (e.GetProperty("namespace").GetString() == "html")
            {
                data.Add(e.GetProperty("tag").GetString()!);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Tags))]
    public void Every_generated_attribute_renders_under_its_MDN_name_in_IDL_order(string tag)
    {
        var element = Build(tag);
        var generated = GeneratedProperties(element.GetType()).ToList();
        foreach (var p in generated)
        {
            p.SetValue(element, SampleFor(p.PropertyType));
        }

        var rendered = RenderedAttributeNames(element.ToHtml(), tag);

        var expected = ExpectedAttributes(tag, element.GetType());
        Assert.Equal(expected, rendered.Where(expected.Contains).ToList());
        Assert.Equal(generated.Count, rendered.Count);
    }

    [Fact]
    public void A_URL_attribute_is_written_through_the_URL_sanitizer()
    {
        var link = Build("a");
        link.GetType().GetProperty("Href")!.SetValue(link, "javascript:alert(1)");

        var html = link.ToHtml();

        Assert.Contains("href=\"about:blank\"", html, StringComparison.Ordinal);
    }

    // The instance an entry builds, which carries its tag (a type several tags share has no tag of its own).
    private static Element Build(string tag)
    {
        var entry = typeof(Element).Assembly.GetTypes()
            .Where(t => typeof(Element).IsAssignableFrom(t) && !t.IsAbstract && !typeof(SvgElement).IsAssignableFrom(t))
            .SelectMany(t => t.GetCustomAttributes<TagAttribute>().Select(a => (Type: t, Attr: a)))
            .Single(x => x.Attr.Name == tag);
        var name = entry.Attr.Entry ?? char.ToUpperInvariant(tag[0]) + tag[1..];
        var property = typeof(Markup).GetProperty(name, BindingFlags.Public | BindingFlags.Static)!;
        var built = property.GetValue(null)!;
        // A typed control's entry is a seed: Of<string>() opens the plain string control, and a form opens on
        // its model.
        if (built is Element element)
        {
            return element;
        }

        var seed = built.GetType();
        return seed.GetMethod("Of") is { } of
            ? (Element)of.MakeGenericMethod(typeof(string)).Invoke(built, null)!
            : (Element)seed.GetMethod("Model")!.MakeGenericMethod(typeof(object)).Invoke(built, [new object()])!;
    }

    // The attribute properties the build generated: declared on an HTML*Element type, plain-typed, settable.
    private static IEnumerable<PropertyInfo> GeneratedProperties(Type type)
    {
        for (var t = type; t is not null && t != typeof(Element); t = t.BaseType)
        {
            if (!t.Name.StartsWith("HTML", StringComparison.Ordinal) || t.IsGenericType)
            {
                continue;
            }

            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (p.CanWrite && (p.PropertyType == typeof(string) || p.PropertyType == typeof(bool?) || p.PropertyType == typeof(int?) || p.PropertyType == typeof(double?))
                    && !(t == typeof(HTMLMetaElement) && p.Name == "Property"))
                {
                    yield return p;
                }
            }
        }
    }

    private static object SampleFor(Type type) =>
        type == typeof(bool?) ? true : type == typeof(int?) ? 7 : type == typeof(double?) ? 1.5 : "v";

    // The tag-specific attributes MDN gives this tag, in render order: each interface's own, base before
    // derived (HTMLMediaElement's, then HTMLVideoElement's), each in IDL order. A typed control's layer owns
    // the attributes it derives from its value (type, name, value, checked, step), so those are its own test's.
    private static List<string> ExpectedAttributes(string tag, Type type)
    {
        var iface = Snapshot.GetProperty("elements").EnumerateArray()
            .First(e => e.GetProperty("namespace").GetString() == "html" && e.GetProperty("tag").GetString() == tag)
            .GetProperty("interface").GetString()!;
        var owned = type.IsGenericType
            ? type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Select(p => p.Name.ToLowerInvariant()).ToHashSet()
            : [];
        var chain = new List<string>();
        for (var t = type; t is not null && t != typeof(HTMLElement); t = t.BaseType)
        {
            if (!t.IsGenericType)
            {
                chain.Insert(0, t.Name);
            }
        }

        var names = new List<string>();
        if (!Snapshot.GetProperty("interfaces").GetProperty(iface).TryGetProperty("attributes", out var attrs))
        {
            return names;
        }

        foreach (var level in chain)
        {
            foreach (var a in attrs.EnumerateArray())
            {
                var on = a.TryGetProperty("on", out var o) && o.GetString() is { } declared && chain.Contains(declared) ? declared : iface;
                var onThisTag = !a.TryGetProperty("tags", out var tags) || tags.EnumerateArray().Any(t => t.GetString() == tag);
                var attr = a.GetProperty("attr").GetString()!;
                // A Rask form submits in-process, so it offers no action or method (DomEmitter.Omitted).
                var omitted = tag == "form" && attr is "action" or "method";
                if (on == level && onThisTag && !owned.Contains(attr) && !omitted)
                {
                    names.Add(attr);
                }
            }
        }

        return names;
    }

    private static List<string> RenderedAttributeNames(string html, string tag)
    {
        var start = Regex.Match(html, "^<" + Regex.Escape(tag) + "(?<attrs>[^>]*)>").Groups["attrs"].Value;
        return Regex.Matches(start, @"\s([a-zA-Z][\w:-]*)(?:=""[^""]*"")?").Select(m => m.Groups[1].Value).ToList();
    }

    private static JsonElement Load()
    {
        for (var dir = AppContext.BaseDirectory; dir is not null; dir = Path.GetDirectoryName(dir))
        {
            if (File.Exists(Path.Combine(dir, "Rask.slnx")))
            {
                return JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "src", "Rask.Core", "Dom", "mdn.snapshot.json"))).RootElement;
            }
        }

        throw new InvalidOperationException("Rask.slnx not found above the test output.");
    }
}
