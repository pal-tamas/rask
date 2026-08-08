using System.Diagnostics;
using System.Net;
using System.Text;

namespace Rask.Testing;

/// <summary>
///     One element in a rendered tree: its tag, its attributes, its children, and the text under it.
/// </summary>
/// <remarks>
///     <para>
///         Deliberately small. This exists so an assertion can say <em>which element</em> a match came
///         from — the one thing <see cref="Markup" />'s attribute scan cannot do — not to be a DOM. There
///         is no mutation, no live collection, and no parent pointer beyond <see cref="Parent" />.
///     </para>
///     <para>
///         Text is exposed as <see cref="Text" /> (this element's own text, decoded) and
///         <see cref="TextContent" /> (that plus every descendant's, which is what an assertion about
///         "what the user reads" usually means).
///     </para>
/// </remarks>
[DebuggerDisplay("{Tag,nq} {DebugAttributes,nq}")]
public sealed class HtmlNode
{
    // Content in document order: each item is either a text run (string) or a child element. Keeping the
    // order matters — `<li><span>7</span> shipped</li>` reads "7 shipped", and a model that buffered all
    // of an element's own text separately from its children would render that as "shipped7".
    private readonly List<object> _content = [];
    private readonly Dictionary<string, string> _attributes = new(StringComparer.OrdinalIgnoreCase);

    internal HtmlNode(string tag) => Tag = tag;

    /// <summary>Lower-case tag name (<c>div</c>, <c>button</c>, …). <c>#root</c> for the synthetic root.</summary>
    public string Tag { get; }

    /// <summary>This element's parent, or <c>null</c> for the synthetic root.</summary>
    public HtmlNode? Parent { get; private set; }

    /// <summary>Child elements, in document order.</summary>
    public IReadOnlyList<HtmlNode> Children => _content.OfType<HtmlNode>().ToArray();

    /// <summary>Attributes, values HTML-decoded. Names are matched case-insensitively.</summary>
    public IReadOnlyDictionary<string, string> Attributes => _attributes;

    /// <summary>The <c>id</c> attribute, or <c>null</c>.</summary>
    public string? Id => Attribute("id");

    /// <summary>The <c>class</c> attribute split on whitespace. Empty when there is no class.</summary>
    public IReadOnlyList<string> Classes =>
        Attribute("class")?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) ?? [];

    /// <summary>This element's own text runs, excluding descendants'. HTML-decoded.</summary>
    public string Text => string.Concat(_content.OfType<string>());

    /// <summary>This element's text plus every descendant's, in document order. HTML-decoded.</summary>
    public string TextContent
    {
        get
        {
            var sb = new StringBuilder();
            Collect(this, sb);
            return sb.ToString();

            static void Collect(HtmlNode node, StringBuilder into)
            {
                foreach (var item in node._content)
                {
                    if (item is HtmlNode child)
                    {
                        Collect(child, into);
                    }
                    else
                    {
                        into.Append((string)item);
                    }
                }
            }
        }
    }

    /// <summary>The value of <paramref name="name" />, or <c>null</c> when the attribute is absent.</summary>
    public string? Attribute(string name) => _attributes.GetValueOrDefault(name);

    /// <summary>True when <c>class</c> contains <paramref name="name" /> as a whole token.</summary>
    public bool HasClass(string name) =>
        Classes.Contains(name, StringComparer.Ordinal);

    /// <summary>This element and every descendant, in document order.</summary>
    public IEnumerable<HtmlNode> DescendantsAndSelf()
    {
        yield return this;
        foreach (var child in _content.OfType<HtmlNode>())
        {
            foreach (var node in child.DescendantsAndSelf())
            {
                yield return node;
            }
        }
    }

    /// <summary>A short path from the root — what a failing assertion prints so you can find the element.</summary>
    public string Path()
    {
        var parts = new List<string>();
        for (var node = this; node is not null && node.Parent is not null; node = node.Parent)
        {
            var part = node.Tag;
            if (node.Id is { Length: > 0 } id)
            {
                part += "#" + id;
            }
            else if (node.Classes.Count > 0)
            {
                part += "." + string.Join(".", node.Classes);
            }

            parts.Insert(0, part);
        }

        return parts.Count == 0 ? Tag : string.Join(" > ", parts);
    }

    internal void Add(HtmlNode child)
    {
        child.Parent = this;
        _content.Add(child);
    }

    internal void SetAttribute(string name, string value) =>
        _attributes[name] = WebUtility.HtmlDecode(value);

    internal void AppendText(string raw) => _content.Add(WebUtility.HtmlDecode(raw));

    private string DebugAttributes =>
        string.Join(" ", _attributes.Select(a => $"{a.Key}=\"{a.Value}\""));
}
