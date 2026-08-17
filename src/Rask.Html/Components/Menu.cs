namespace Rask.Html.Components;

/// <summary>
///     A semantic alternative to <c>ul</c> for a toolbar-like list of commands. Browsers treat it exactly
///     as an unordered list.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/menu">MDN</see>
/// </summary>
public sealed partial class Menu : Element
{
    protected override string TagName => "menu";
}
