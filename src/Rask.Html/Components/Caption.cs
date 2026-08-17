namespace Rask.Html.Components;

/// <summary>
///     A table's title. Must be the first child of its <c>table</c>, and is the accessible name screen
///     readers announce for it.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/caption">MDN</see>
/// </summary>
public sealed partial class Caption : Element
{
    protected override string TagName => "caption";
}
