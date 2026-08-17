namespace Rask.Core.Components;

// Div carries no tag-specific attributes or handlers of its own: OnClick and OnScroll (and the rest of
// the GlobalEventHandlers surface) are inherited from Element, like every other element.

/// <summary>
///     A generic block container with no meaning of its own. Reach for it only when no semantic element
///     fits — <c>section</c>, <c>article</c>, <c>nav</c> and <c>main</c> tell assistive technology
///     something a <c>div</c> cannot.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/div">MDN</see>
/// </summary>
public sealed class Div : Element
{
    protected override string TagName => "div";
}
