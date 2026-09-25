using System.Text;

namespace Rask.Core.Components;

// What MDN's HTMLMetaElement (generated) cannot carry: Open Graph's `property`, which comes from RDFa, not
// HTML, so neither the spec nor MDN lists it — and a page's link previews depend on it.
public sealed partial class HTMLMetaElement
{
    /// <summary>
    ///     The metadata property, for the vocabularies that name themselves with <c>property</c> rather
    ///     than <c>name</c> — Open Graph (<c>og:title</c>, <c>og:image</c>) and the RDFa-shaped tags that
    ///     follow it (<c>article:published_time</c>, <c>product:price:amount</c>).
    /// </summary>
    /// <remarks>
    ///     A separate property rather than a spelling of <c>Name</c>: a crawler reading Open Graph looks for
    ///     <c>property</c> and does not fall back, so writing an <c>og:</c> value into <c>name</c> produces a
    ///     tag that validates, renders, and is ignored by every consumer it was written for.
    /// </remarks>
    public string? Property { get; set; }

    // Before the generated attributes: `<meta property="og:title" content="…">` is the form every Open Graph
    // tag is written and read in.
    partial void WriteOwnedAttributesFirst(StringBuilder sb)
    {
        if (Property is not null)
        {
            AppendAttr(sb, "property", Property);
        }
    }
}
