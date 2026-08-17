namespace Rask.Html.Components;

/// <summary>
///     Art-directed or format-negotiated images: <c>source</c> children offer candidates, and the required
///     <c>img</c> child is both the fallback and the element that actually renders.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/picture">MDN</see>
/// </summary>
public sealed partial class Picture : Element
{
    protected override string TagName => "picture";
}
