namespace Rask.Core.Components;

/// <summary>
///     A caption for its parent <c>figure</c>. Must be that figure's first or last child.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/figcaption">MDN</see>
/// </summary>
public sealed class Figcaption : Element
{
    protected override string TagName => "figcaption";
}
