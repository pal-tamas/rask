namespace Rask.Core.Components;

// Container for reusable definitions (gradients, patterns, clip paths) referenced elsewhere.

/// <summary>
///     Definitions that are never rendered where they sit — gradients, filters, markers, symbols — to be
///     referenced by <c>id</c> from elsewhere in the document.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Element/defs">MDN</see>
/// </summary>
public sealed class Defs : SvgElement
{
    protected override string TagName => "defs";
}
