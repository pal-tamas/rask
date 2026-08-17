namespace Rask.Html.Components;

// Renders the first child whose conditional attributes are satisfied.

/// <summary>
///     Renders the first child whose <c>requiredFeatures</c>, <c>requiredExtensions</c> and
///     <c>systemLanguage</c> all pass, and skips the rest.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Element/switch">MDN</see>
/// </summary>
public sealed partial class Switch : SvgElement
{
    protected override string TagName => "switch";
}
