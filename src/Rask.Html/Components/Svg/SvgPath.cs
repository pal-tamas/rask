using System.Text;

namespace Rask.Html.Components;

// SVG <path>. Named SvgPath to avoid colliding with System.IO.Path, which would otherwise be
// shadowed by the generated Path(...) factory wherever the Generated static using is imported.
public sealed partial class SvgPath : SvgElement
{
    protected override string TagName => "path";

    public string? D { get; set; }
    public string? PathLength { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (D is not null)
        {
            AppendAttr(sb, "d", D);
        }

        if (PathLength is not null)
        {
            AppendAttr(sb, "pathLength", PathLength);
        }
    }
}
