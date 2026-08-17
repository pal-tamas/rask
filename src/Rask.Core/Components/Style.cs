using System.Text;

namespace Rask.Core.Components;

/// <summary>
///     Inline CSS for the document. Rask's own scoped stylesheets arrive as generated <c>link</c> elements,
///     so this is for your own rules.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/style">MDN</see>
/// </summary>
public sealed class Style : Element
{
    protected override string TagName => "style";

    /// <summary>
    ///     The stylesheet language. Omit it — the only valid value is the default, <c>text/css</c>.
    /// </summary>
    public string? Type { get; set; }

    /// <summary>A media query limiting when the styles apply.</summary>
    public string? Media { get; set; }

    // `title` on <style> names an alternative stylesheet, but it is the same global attribute every
    // element has — so it is inherited from Element rather than redeclared here, and renders in the
    // global slot (with id/class/style) instead of among the tag-specific ones.

    /// <summary>A cryptographic nonce that lets this block run under a Content Security Policy.</summary>
    public string? Nonce { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Type is not null)
        {
            AppendAttr(sb, "type", Type);
        }

        if (Media is not null)
        {
            AppendAttr(sb, "media", Media);
        }

        if (Nonce is not null)
        {
            AppendAttr(sb, "nonce", Nonce);
        }
    }
}
