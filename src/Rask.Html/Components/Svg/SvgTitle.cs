namespace Rask.Html.Components;

// SVG <title> (accessible name / tooltip). Named SvgTitle to avoid colliding with the HTML
// Title component used in the document head.
public sealed partial class SvgTitle : SvgElement
{
    protected override string TagName => "title";
}
