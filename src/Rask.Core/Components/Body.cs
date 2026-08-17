namespace Rask.Core.Components;

/// <summary>
///     The document's visible content. Exactly one per page, and in Rask a page's root render must emit the
///     whole shell — <c>Doctype</c>, <c>Html</c>, <c>Head</c>, <c>Body</c> — which RASK021 enforces.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/body">MDN</see>
/// </summary>
public sealed class Body : Element
{
    protected override string TagName => "body";
}
