namespace Rask.Core.Components;

/// <summary>
///     A block of major navigation links. Meant for the primary sets — a site menu, a table of contents —
///     not every group of links on the page.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/nav">MDN</see>
/// </summary>
public sealed class Nav : Element
{
    protected override string TagName => "nav";
}
