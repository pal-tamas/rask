using System.Text;

namespace Rask.Core.Components;

/// <summary>
///     The root of an SVG fragment, and a container for nesting one inside another. Give it a
///     <c>ViewBox</c> and no fixed <c>Width</c>/<c>Height</c> to get a graphic that scales with its
///     container.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Element/svg">MDN</see>
/// </summary>
public sealed class Svg : SvgElement
{
    protected override string TagName => "svg";

    /// <summary>
    ///     The rendered width. Omit it, with a <c>ViewBox</c> set, to let CSS size the graphic.
    /// </summary>
    public string? Width { get; set; }

    /// <summary>
    ///     The rendered height. Omit it, with a <c>ViewBox</c> set, to let CSS size the graphic.
    /// </summary>
    public string? Height { get; set; }

    /// <summary>
    ///     The user-coordinate rectangle mapped onto the viewport, as <c>min-x min-y width height</c>. This
    ///     is what makes an SVG resolution-independent.
    /// </summary>
    public string? ViewBox { get; set; }

    /// <summary>
    ///     How to fit the <c>ViewBox</c> into the viewport when their aspect ratios differ — an alignment
    ///     such as <c>xMidYMid</c> plus <c>meet</c> or <c>slice</c>.
    /// </summary>
    public string? PreserveAspectRatio { get; set; }

    /// <summary>The x offset of a nested <c>svg</c>. Ignored on the outermost one.</summary>
    public string? X { get; set; }

    /// <summary>The y offset of a nested <c>svg</c>. Ignored on the outermost one.</summary>
    public string? Y { get; set; }

    /// <summary>
    ///     The SVG namespace. Required only when the document is served as XML rather than HTML.
    /// </summary>
    public string? Xmlns { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Width is not null)
        {
            AppendAttr(sb, "width", Width);
        }

        if (Height is not null)
        {
            AppendAttr(sb, "height", Height);
        }

        if (ViewBox is not null)
        {
            AppendAttr(sb, "viewBox", ViewBox);
        }

        if (PreserveAspectRatio is not null)
        {
            AppendAttr(sb, "preserveAspectRatio", PreserveAspectRatio);
        }

        if (X is not null)
        {
            AppendAttr(sb, "x", X);
        }

        if (Y is not null)
        {
            AppendAttr(sb, "y", Y);
        }

        if (Xmlns is not null)
        {
            AppendAttr(sb, "xmlns", Xmlns);
        }
    }
}
