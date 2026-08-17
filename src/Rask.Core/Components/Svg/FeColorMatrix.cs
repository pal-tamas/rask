using System.Text;

namespace Rask.Core.Components;

/// <summary>
///     Transforms the input's colours with a matrix — how you desaturate, tint, or shift the hue of a
///     graphic.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Element/feColorMatrix">MDN</see>
/// </summary>
public sealed class FeColorMatrix : SvgElement
{
    protected override string TagName => "feColorMatrix";

    /// <summary>The input for this primitive.</summary>
    public string? In { get; set; }

    /// <summary><c>matrix</c>, <c>saturate</c>, <c>hueRotate</c>, or <c>luminanceToAlpha</c>.</summary>
    public string? Type { get; set; }

    /// <summary>
    ///     The value for the chosen <c>Type</c>: twenty numbers for a matrix, a single number for
    ///     saturation or hue rotation.
    /// </summary>
    public string? Values { get; set; }

    /// <summary>A name for this primitive's output.</summary>
    public string? Result { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (In is not null)
        {
            AppendAttr(sb, "in", In);
        }

        if (Type is not null)
        {
            AppendAttr(sb, "type", Type);
        }

        if (Values is not null)
        {
            AppendAttr(sb, "values", Values);
        }

        if (Result is not null)
        {
            AppendAttr(sb, "result", Result);
        }
    }
}
