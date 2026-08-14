namespace Rask.Core.Components;

/// <summary>
///     The defining instance of a term — where the surrounding text explains what it means.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/dfn">MDN</see>
/// </summary>
public sealed class Dfn : Element
{
    protected override string TagName => "dfn";
}
