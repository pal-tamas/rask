namespace Rask.Core.Components;

/// <summary>
///     The caption for a <c>fieldset</c>. Must be that fieldset's first child.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/legend">MDN</see>
/// </summary>
public sealed class Legend : Element
{
    protected override string TagName => "legend";
}
