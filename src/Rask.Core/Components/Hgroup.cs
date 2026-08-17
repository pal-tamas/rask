namespace Rask.Core.Components;

/// <summary>
///     A heading together with the paragraphs that stand in for a subtitle or tagline, so only the heading
///     enters the document outline.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/hgroup">MDN</see>
/// </summary>
public sealed class Hgroup : Element
{
    protected override string TagName => "hgroup";
}
