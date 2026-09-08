using System.Text;

namespace Rask.Core.Components;

/// <summary>
///     Document metadata that no other element can express — the character set, the viewport, the
///     description, Open Graph tags.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/meta">MDN</see>
/// </summary>
public sealed class Meta : Element
{
    protected override string TagName => "meta";
    protected override bool SelfClosing => true;

    /// <summary>
    ///     The document's character encoding. Use <c>utf-8</c>, and put it in the first 1024 bytes of the
    ///     document.
    /// </summary>
    public string? Charset { get; set; }

    /// <summary>
    ///     The metadata name — <c>viewport</c>, <c>description</c>, <c>theme-color</c>, <c>robots</c> —
    ///     whose value is <c>Content</c>.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    ///     The metadata property, for the vocabularies that name themselves with <c>property</c> rather
    ///     than <c>name</c> — Open Graph (<c>og:title</c>, <c>og:image</c>) and the RDFa-shaped tags that
    ///     follow it (<c>article:published_time</c>, <c>product:price:amount</c>).
    /// </summary>
    /// <remarks>
    ///     A separate property rather than a spelling of <see cref="Name" />: a crawler reading Open
    ///     Graph looks for <c>property</c> and does not fall back, so writing an <c>og:</c> value into
    ///     <c>name</c> produces a tag that validates, renders, and is ignored by every consumer it was
    ///     written for.
    /// </remarks>
    public string? Property { get; set; }

    /// <summary>The value for <c>Name</c>, <c>Property</c> or <c>HttpEquiv</c>.</summary>
    public string? Content { get; set; }

    /// <summary>
    ///     A pragma directive that acts like an HTTP response header, such as
    ///     <c>content-security-policy</c> or <c>refresh</c>.
    /// </summary>
    public string? HttpEquiv { get; set; }

    /// <summary>
    ///     The media query this value applies to. Its one real use is a light/dark
    ///     <c>theme-color</c> pair, which is two tags with the same name and different values.
    /// </summary>
    public string? Media { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Charset is not null)
        {
            AppendAttr(sb, "charset", Charset);
        }

        if (Name is not null)
        {
            AppendAttr(sb, "name", Name);
        }

        if (Property is not null)
        {
            AppendAttr(sb, "property", Property);
        }

        if (Content is not null)
        {
            AppendAttr(sb, "content", Content);
        }

        if (HttpEquiv is not null)
        {
            AppendAttr(sb, "http-equiv", HttpEquiv);
        }

        if (Media is not null)
        {
            AppendAttr(sb, "media", Media);
        }
    }
}
