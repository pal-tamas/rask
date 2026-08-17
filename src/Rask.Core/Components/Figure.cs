namespace Rask.Core.Components;

/// <summary>
///     Self-contained referenced content — an image, a diagram, a code listing — optionally captioned by a
///     <c>figcaption</c>. It should be movable elsewhere without breaking the flow of the text.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/figure">MDN</see>
/// </summary>
public sealed class Figure : Element
{
    protected override string TagName => "figure";
}
