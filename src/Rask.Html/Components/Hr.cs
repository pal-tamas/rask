namespace Rask.Html.Components;

/// <summary>
///     A thematic break — a shift in topic within a section. Semantic, not decorative: for a plain rule,
///     use a CSS border.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/hr">MDN</see>
/// </summary>
public sealed partial class Hr : Element
{
    protected override string TagName => "hr";
    protected override bool SelfClosing => true;
}
