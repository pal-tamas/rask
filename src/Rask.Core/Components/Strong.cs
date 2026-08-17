namespace Rask.Core.Components;

/// <summary>
///     Content of strong importance, seriousness or urgency. For stress emphasis use <c>em</c>; for bold
///     text with neither meaning, use CSS.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/strong">MDN</see>
/// </summary>
public sealed class Strong : Element
{
    protected override string TagName => "strong";
}
