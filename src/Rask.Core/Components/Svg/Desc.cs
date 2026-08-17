namespace Rask.Core.Components;

// Accessible long-form description for its parent SVG element.

/// <summary>
///     A long description of its parent element, for assistive technology. Pair it with a <c>title</c>,
///     which supplies the short name.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Element/desc">MDN</see>
/// </summary>
public sealed class Desc : SvgElement
{
    protected override string TagName => "desc";
}
