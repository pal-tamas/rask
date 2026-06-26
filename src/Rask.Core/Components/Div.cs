namespace Rask.Core.Components;

// Div carries no tag-specific attributes or handlers of its own: OnClick and OnScroll (and the rest of
// the GlobalEventHandlers surface) are inherited from Element, like every other element.
public sealed class Div : Element
{
    protected override string TagName => "div";
}
