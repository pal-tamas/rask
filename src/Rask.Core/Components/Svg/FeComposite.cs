using System.Text;

namespace Rask.Core.Components;

/// <summary>
///     Combines two inputs with a Porter-Duff compositing operator, or with the arithmetic operator and its
///     four coefficients.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Element/feComposite">MDN</see>
/// </summary>
public sealed class FeComposite : SvgElement
{
    protected override string TagName => "feComposite";

    /// <summary>The first input.</summary>
    public string? In { get; set; }

    /// <summary>The second input.</summary>
    public string? In2 { get; set; }

    /// <summary>
    ///     <c>over</c>, <c>in</c>, <c>out</c>, <c>atop</c>, <c>xor</c>, or <c>arithmetic</c>.
    /// </summary>
    public string? Operator { get; set; }

    /// <summary>
    ///     The first arithmetic coefficient. Used only when <c>Operator</c> is <c>arithmetic</c>.
    /// </summary>
    public string? K1 { get; set; }

    /// <summary>The second arithmetic coefficient.</summary>
    public string? K2 { get; set; }

    /// <summary>The third arithmetic coefficient.</summary>
    public string? K3 { get; set; }

    /// <summary>The fourth arithmetic coefficient.</summary>
    public string? K4 { get; set; }

    /// <summary>A name for this primitive's output.</summary>
    public string? Result { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (In is not null)
        {
            AppendAttr(sb, "in", In);
        }

        if (In2 is not null)
        {
            AppendAttr(sb, "in2", In2);
        }

        if (Operator is not null)
        {
            AppendAttr(sb, "operator", Operator);
        }

        if (K1 is not null)
        {
            AppendAttr(sb, "k1", K1);
        }

        if (K2 is not null)
        {
            AppendAttr(sb, "k2", K2);
        }

        if (K3 is not null)
        {
            AppendAttr(sb, "k3", K3);
        }

        if (K4 is not null)
        {
            AppendAttr(sb, "k4", K4);
        }

        if (Result is not null)
        {
            AppendAttr(sb, "result", Result);
        }
    }
}
