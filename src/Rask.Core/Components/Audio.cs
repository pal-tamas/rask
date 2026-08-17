namespace Rask.Core.Components;

/// <summary>
///     Embedded sound. Offer several encodings as <c>source</c> children, and put fallback text inside for
///     browsers that cannot play any of them. Autoplay with sound is blocked by every modern browser until
///     the user has interacted with the page.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/audio">MDN</see>
/// </summary>
public sealed class Audio : HtmlMediaElement
{
    protected override string TagName => "audio";
}
