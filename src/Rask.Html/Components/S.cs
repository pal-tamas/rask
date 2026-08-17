namespace Rask.Html.Components;

/// <summary>
///     Text no longer accurate or relevant. For an edit to the document, use <c>del</c> instead — it
///     records who changed what and when.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/s">MDN</see>
/// </summary>
public sealed partial class S : Element
{
    protected override string TagName => "s";
}
