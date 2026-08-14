namespace Rask.Core.Components;

/// <summary>
///     The document's metadata: <c>title</c>, <c>meta</c>, <c>link</c>, <c>style</c>, <c>script</c>. None
///     of it renders. Rask appends its runtime <c>script</c> to the page automatically.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/head">MDN</see>
/// </summary>
public sealed class Head : Element
{
    protected override string TagName => "head";
}
