using System.Text;

namespace Rask.Core.Components;

// Shared base for the document-modification elements (Ins, Del), mirroring the DOM
// `HTMLModElement` interface. Both carry the same cite/datetime attributes; neither adds extras,
// so they derive from this base with no body of their own. `cite` is URL-sanitized.

/// <summary>
///     The attributes <c>ins</c> and <c>del</c> share — the two elements that record an edit to the
///     document. Not a tag of its own. <see
///     href="https://developer.mozilla.org/en-US/docs/Web/API/HTMLModElement">MDN: HTMLModElement</see>
/// </summary>
public abstract class HtmlModElement : Element
{
    /// <summary>A URL explaining the change — an issue, a changelog entry.</summary>
    public new string? Cite { get; set; }

    /// <summary>When the change was made, as a machine-readable date or datetime.</summary>
    public string? DateTime { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Cite is not null)
        {
            AppendUrlAttr(sb, "cite", Cite);
        }

        if (DateTime is not null)
        {
            AppendAttr(sb, "datetime", DateTime);
        }
    }
}
