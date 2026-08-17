using System.Text;

namespace Rask.Html.Components;

/// <summary>
///     A header cell. Set <c>Scope</c> so screen readers know which cells it heads — the single most useful
///     thing you can do for a data table.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/th">MDN</see>
/// </summary>
public sealed partial class Th : HtmlTableCellElement
{
    protected override string TagName => "th";

    /// <summary>
    ///     Which cells this header describes: <c>col</c>, <c>row</c>, <c>colgroup</c>, or <c>rowgroup</c>.
    /// </summary>
    public string? Scope { get; set; }

    /// <summary>A short form of the header, used where repeating the full text would be tedious.</summary>
    public new string? Abbr { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        // Emits the universal attrs then the shared HtmlTableCellElement block (colspan, rowspan,
        // headers); the th-specific scope/abbr follow that block.
        base.WriteAttributes(sb);

        if (Scope is not null)
        {
            AppendAttr(sb, "scope", Scope);
        }

        if (Abbr is not null)
        {
            AppendAttr(sb, "abbr", Abbr);
        }
    }
}
