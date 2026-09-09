using System.Text;

namespace Rask.Html.Components;

// The SVG DOM's own grouping, not one invented here: MDN defines SVGGeometryElement as the interface
// for every shape whose outline is a geometry the browser can measure, and lists exactly seven —
// circle, ellipse, line, path, polygon, polyline, rect. All seven declared `PathLength` themselves
// before this existed, which is the duplication a shared interface is for.
//
// It sits where MDN puts it, under the graphics elements, and carries only what the interface carries.
// The shapes' own geometry (cx/cy/r, x/y/width/height, d, points) stays on the concrete tags, because
// that is where the DOM puts it too.

/// <summary>
///     The shapes that have a measurable outline — <c>circle</c>, <c>ellipse</c>, <c>line</c>,
///     <c>path</c>, <c>polygon</c>, <c>polyline</c> and <c>rect</c>. Not a tag of its own: it exists so
///     the one attribute they share is declared once.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/SVGGeometryElement">MDN</see>
/// </summary>
public abstract partial class SvgGeometryElement : SvgElement
{
    /// <summary>
    ///     The length the browser should pretend the outline has, so dash patterns can be expressed as
    ///     fractions of it.
    /// </summary>
    public string? PathLength { get; set; }

    /// <inheritdoc />
    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (PathLength is not null)
        {
            AppendAttr(sb, "pathLength", PathLength);
        }
    }
}
