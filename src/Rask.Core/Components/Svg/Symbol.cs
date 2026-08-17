using System.Text;

namespace Rask.Core.Components;

/// <summary>
///     A template that is never rendered until a <c>use</c> references it. Unlike a bare group, it can
///     carry its own <c>ViewBox</c>, so each instance scales independently — which is what makes it the
///     right container for an icon sprite.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Element/symbol">MDN</see>
/// </summary>
public sealed class Symbol : SvgElement
{
    protected override string TagName => "symbol";

    /// <summary>
    ///     The user-coordinate rectangle this symbol maps onto whatever viewport a <c>use</c> gives it.
    /// </summary>
    public string? ViewBox { get; set; }

    /// <summary>How to fit the <c>ViewBox</c> when the aspect ratios differ.</summary>
    public string? PreserveAspectRatio { get; set; }

    /// <summary>The x offset of the symbol's viewport.</summary>
    public string? X { get; set; }

    /// <summary>The y offset of the symbol's viewport.</summary>
    public string? Y { get; set; }

    /// <summary>The symbol's viewport width.</summary>
    public string? Width { get; set; }

    /// <summary>The symbol's viewport height.</summary>
    public string? Height { get; set; }

    /// <summary>
    ///     The x coordinate inside the symbol that lines up with the <c>use</c> element's x position.
    /// </summary>
    public string? RefX { get; set; }

    /// <summary>
    ///     The y coordinate inside the symbol that lines up with the <c>use</c> element's y position.
    /// </summary>
    public string? RefY { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
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

        if (Width is not null)
        {
            AppendAttr(sb, "width", Width);
        }

        if (Height is not null)
        {
            AppendAttr(sb, "height", Height);
        }

        if (RefX is not null)
        {
            AppendAttr(sb, "refX", RefX);
        }

        if (RefY is not null)
        {
            AppendAttr(sb, "refY", RefY);
        }
    }
}
