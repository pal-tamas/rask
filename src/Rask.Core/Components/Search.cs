namespace Rask.Core.Components;

/// <summary>
///     A set of controls that perform a search or filter. Carries the <c>search</c> ARIA role natively, so
///     it needs no <c>Role</c> of its own.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/search">MDN</see>
/// </summary>
public sealed class Search : Element
{
    protected override string TagName => "search";
}
