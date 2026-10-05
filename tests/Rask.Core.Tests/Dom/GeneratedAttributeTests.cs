using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Rask.Core.Tests.Dom;

// Every attribute property the build generated from MDN, on every HTML and SVG tag, rendered at once: the
// attributes must come out under MDN's names, in IDL order, after the globals. One test in place of a hundred
// per-tag files, because the tags are data now — a new one arrives with a snapshot refresh and is covered here.
public partial class GeneratedAttributeTests
{
    private static readonly JsonElement Snapshot = MdnSnapshot.Root;

    // "html:a", "svg:a": the two namespaces share tag names.
    public static TheoryData<string> Tags()
    {
        var data = new TheoryData<string>();
        foreach (var e in Snapshot.GetProperty("elements").EnumerateArray())
        {
            data.Add(e.GetProperty("namespace").GetString() + ":" + e.GetProperty("tag").GetString());
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Tags))]
    public void Every_generated_attribute_renders_under_its_MDN_name_in_IDL_order(string element)
    {
        var (ns, tag) = Split(element);
        var built = Build(ns, tag);
        var generated = GeneratedProperties(built.GetType()).ToList();
        foreach (var p in generated)
        {
            p.SetValue(built, SampleFor(p.PropertyType));
        }

        var rendered = RenderedAttributeNames(built.ToHtml(), tag);

        var expected = ExpectedAttributes(ns, tag, built.GetType());
        Assert.Equal(expected, rendered.Where(expected.Contains).ToList());
        Assert.Equal(generated.Count, rendered.Count);
    }

    [Theory]
    [InlineData("html", "a")]
    [InlineData("svg", "a")]
    [InlineData("svg", "use")]
    public void A_URL_attribute_is_written_through_the_URL_sanitizer(string ns, string tag)
    {
        var link = Build(ns, tag);
        link.GetType().GetProperty("Href")!.SetValue(link, "javascript:alert(1)");

        var html = link.ToHtml();

        Assert.Contains("href=\"about:blank\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void No_generated_attribute_is_an_inline_event_handler()
    {
        var types = typeof(Element).Assembly.GetTypes()
            .Where(t => typeof(Element).IsAssignableFrom(t) && (t.Name.StartsWith("HTML", StringComparison.Ordinal) || t.Name.StartsWith("SVG", StringComparison.Ordinal)));

        // SVG's onbegin/onend/onrepeat are attributes in MDN's data: inline JavaScript, which Rask never writes.
        var handlers = types.SelectMany(GeneratedProperties).Where(p => Regex.IsMatch(p.Name, "^On[a-z]")).Select(p => $"{p.DeclaringType!.Name}.{p.Name}").Distinct().ToList();

        Assert.Empty(handlers);
    }

    [Fact]
    public void An_SVG_image_may_inline_its_picture_as_a_data_URL()
    {
        var image = Build("svg", "image");
        image.GetType().GetProperty("Href")!.SetValue(image, "data:image/png;base64,AAAA");

        var html = image.ToHtml();

        Assert.Contains("href=\"data:image/png;base64,AAAA\"", html, StringComparison.Ordinal);
    }

    private static (string Namespace, string Tag) Split(string element) =>
        (element[..element.IndexOf(':')], element[(element.IndexOf(':') + 1)..]);

    // The instance an entry builds, which carries its tag (a type several tags share has no tag of its own).
    private static Element Build(string ns, string tag)
    {
        var entry = typeof(Element).Assembly.GetTypes()
            .Where(t => typeof(Element).IsAssignableFrom(t) && !t.IsAbstract && typeof(SVGElement).IsAssignableFrom(t) == (ns == "svg"))
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

    // The attribute properties the build generated: declared on an HTML*/SVG*Element type, plain-typed, settable.
    // SVGElement's inline presentation attributes are hand-written, and render all the same.
    private static IEnumerable<PropertyInfo> GeneratedProperties(Type type)
    {
        for (var t = type; t is not null && t != typeof(Element); t = t.BaseType)
        {
            if (!(t.Name.StartsWith("HTML", StringComparison.Ordinal) || t.Name.StartsWith("SVG", StringComparison.Ordinal)) || t.IsGenericType)
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
    private static List<string> ExpectedAttributes(string ns, string tag, Type type)
    {
        var iface = Snapshot.GetProperty("elements").EnumerateArray()
            .First(e => e.GetProperty("namespace").GetString() == ns && e.GetProperty("tag").GetString() == tag)
            .GetProperty("interface").GetString()!;
        // A typed control's layer owns the attributes it derives from its value; SVGElement's partial owns the
        // presentation attributes (`fill`, which BCD files per shape).
        var owned = type.IsGenericType
            ? type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Select(p => p.Name.ToLowerInvariant()).ToHashSet()
            : ns == "svg" ? typeof(SVGElement).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Select(p => p.Name.ToLowerInvariant()).ToHashSet() : [];
        var chain = new List<string>();
        for (var t = type; t is not null && t != typeof(HTMLElement) && t != typeof(SVGElement); t = t.BaseType)
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
                // …and Element's own `title` is the one SVG's <style> writes.
                var omitted = (tag == "form" && attr is "action" or "method") || (ns == "svg" && attr == "title");
                if (on == level && onThisTag && !owned.Contains(attr.Replace("-", "", StringComparison.Ordinal).ToLowerInvariant()) && !omitted)
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
}
