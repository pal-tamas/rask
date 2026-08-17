using System.Text;

namespace Rask.Html.Components;

// SVG <path>. Named SvgPath to avoid colliding with System.IO.Path, which would otherwise be
// shadowed by the generated Path(...) factory wherever the Generated static using is imported.

/// <summary>
///     An arbitrary shape described by the path commands in <c>D</c> — the most general SVG shape, and what
///     icon sets are made of. Named <c>SvgPath</c> so it does not collide with <c>System.IO.Path</c>; it
///     still renders as <c>path</c>.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Element/path">MDN</see>
/// </summary>
public sealed partial class SvgPath : SvgElement
{
    protected override string TagName => "path";

    /// <summary>
    ///     The path data: move, line, curve and arc commands (<c>M</c>, <c>L</c>, <c>C</c>, <c>A</c>,
    ///     <c>Z</c>, …). Uppercase commands are absolute, lowercase relative.
    /// </summary>
    public string? D { get; set; }

    /// <summary>
    ///     The length the browser should pretend the path has, for dash patterns expressed as fractions.
    /// </summary>
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
