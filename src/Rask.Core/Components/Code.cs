namespace Rask.Core.Components;

/// <summary>
///     A fragment of computer code. For a multi-line block, wrap it in <c>pre</c> to keep the whitespace.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/code">MDN</see>
/// </summary>
public sealed class Code : Element
{
    protected override string TagName => "code";
}
