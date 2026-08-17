namespace Rask.Html.Components;

/// <summary>
///     The document's dominant content — what is unique to this page, excluding the nav, banner and footer
///     repeated across the site. One per page, and the target every skip link should point at.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/main">MDN</see>
/// </summary>
public sealed partial class Main : Element
{
    protected override string TagName => "main";
}
