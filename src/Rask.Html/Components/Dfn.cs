namespace Rask.Html.Components;

/// <summary>
///     The defining instance of a term — where the surrounding text explains what it means.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/dfn">MDN</see>
/// </summary>
public sealed partial class Dfn : Element
{
    protected override string TagName => "dfn";
}
