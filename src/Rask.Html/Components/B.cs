namespace Rask.Html.Components;

/// <summary>
///     Text drawn attention to for utilitarian reasons, carrying no extra importance — a keyword, a product
///     name, a lede. When the text really is important, use <c>strong</c>; when it is merely styled, use
///     CSS.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/b">MDN</see>
/// </summary>
public sealed partial class B : Element
{
    protected override string TagName => "b";
}
