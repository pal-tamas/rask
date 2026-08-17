namespace Rask.Core.Components;

/// <summary>
///     The document's title: the browser tab, the bookmark, the search result, and the first thing a screen
///     reader announces on load. Exactly one, inside <c>head</c>.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/title">MDN</see>
/// </summary>
public sealed class Title : Element
{
    protected override string TagName => "title";
}
