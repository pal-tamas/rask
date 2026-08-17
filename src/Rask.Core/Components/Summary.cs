namespace Rask.Core.Components;

/// <summary>
///     The always-visible label and toggle of a <c>details</c> disclosure. Must be that element's first
///     child.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/summary">MDN</see>
/// </summary>
public sealed class Summary : Element
{
    protected override string TagName => "summary";
}
