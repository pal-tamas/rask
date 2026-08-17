namespace Rask.Core.Components;

// SVG group container. Carries only the inherited presentation attributes (notably Transform).

/// <summary>
///     Groups child elements so a transform, a style or a set of presentation attributes applies to all of
///     them at once.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Element/g">MDN</see>
/// </summary>
public sealed class G : SvgElement
{
    protected override string TagName => "g";
}
