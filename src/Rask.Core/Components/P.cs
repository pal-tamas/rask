namespace Rask.Core.Components;

/// <summary>
///     A paragraph. Cannot contain block-level content — an unclosed nesting attempt is silently split by
///     the parser.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/p">MDN</see>
/// </summary>
public sealed class P : Element
{
    protected override string TagName => "p";
}
