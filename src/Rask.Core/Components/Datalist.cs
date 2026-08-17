namespace Rask.Core.Components;

/// <summary>
///     A set of suggested <c>option</c> values for an input, referenced by that input's <c>list</c>
///     attribute. Suggestions only — unlike <c>select</c>, the user may still type anything.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/datalist">MDN</see>
/// </summary>
public sealed class Datalist : Element
{
    protected override string TagName => "datalist";
}
