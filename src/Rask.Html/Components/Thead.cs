namespace Rask.Html.Components;

/// <summary>
///     The table's header rows. Browsers repeat it on every printed page.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/thead">MDN</see>
/// </summary>
public sealed partial class Thead : HtmlTableSectionElement
{
    protected override string TagName => "thead";
}
