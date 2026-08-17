namespace Rask.Html.Components;

// Composites its feMergeNode children. Carries only inherited attributes.

/// <summary>
///     Stacks several inputs on top of one another, given as <c>feMergeNode</c> children — the last step of
///     a hand-built shadow, where the shadow and the original are put back together.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Element/feMerge">MDN</see>
/// </summary>
public sealed partial class FeMerge : SvgElement
{
    protected override string TagName => "feMerge";
}
