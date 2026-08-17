namespace Rask.Html.Components;

/// <summary>
///     Text with a non-textual annotation — a misspelling, a proper name in Chinese. Underlining otherwise
///     reads as a link, so reach for it rarely.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/u">MDN</see>
/// </summary>
public sealed partial class U : Element
{
    protected override string TagName => "u";
}
