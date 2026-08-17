namespace Rask.Core.Components;

/// <summary>
///     A term in a description list, described by the <c>dd</c> elements that follow it.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/dt">MDN</see>
/// </summary>
public sealed class Dt : Element
{
    protected override string TagName => "dt";
}
