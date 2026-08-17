using System.Text;

namespace Rask.Html.Components;

public sealed partial class Th : HtmlTableCellElement
{
    protected override string TagName => "th";

    public string? Scope { get; set; }
    public string? Abbr { get; set; }

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
