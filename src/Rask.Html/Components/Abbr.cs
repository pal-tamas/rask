namespace Rask.Html.Components;

/// <summary>
///     An abbreviation or acronym. Put the expansion in <c>Title</c> so the full form is available on hover
///     — and repeat it in the text on first use, since <c>title</c> never reaches touch users.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/abbr">MDN</see>
/// </summary>
public sealed partial class Abbr : Element
{
    protected override string TagName => "abbr";
}
