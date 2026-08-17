namespace Rask.Core.Components;

// SVG <title> (accessible name / tooltip). Named SvgTitle to avoid colliding with the HTML
// Title component used in the document head.

/// <summary>
///     The accessible name of its parent element — the SVG equivalent of an image's <c>alt</c>. Named
///     <c>SvgTitle</c> so it does not collide with the HTML <c>title</c>; make it the first child of the
///     element it names.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Element/title">MDN</see>
/// </summary>
public sealed class SvgTitle : SvgElement
{
    protected override string TagName => "title";
}
