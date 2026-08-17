namespace Rask.Core.Components;

/// <summary>
///     A row of cells in a table — <c>th</c> or <c>td</c> children.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/tr">MDN</see>
/// </summary>
public sealed class Tr : Element
{
    protected override string TagName => "tr";
}
