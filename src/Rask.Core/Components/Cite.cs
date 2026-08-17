namespace Rask.Core.Components;

/// <summary>
///     The title of a cited work — a book, a paper, a film. Names a work, not a person.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/cite">MDN</see>
/// </summary>
public sealed class Cite : Element
{
    protected override string TagName => "cite";
}
