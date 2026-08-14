namespace Rask.Core.Components;

/// <summary>
///     Content shown only when scripting is unavailable. Rask's live runtime needs script, so this is where
///     a static fallback or an explanation belongs.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/noscript">MDN</see>
/// </summary>
public sealed class Noscript : Element
{
    protected override string TagName => "noscript";
}
