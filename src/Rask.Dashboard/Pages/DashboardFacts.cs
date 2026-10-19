namespace Rask.Dashboard.Pages;

/// <summary>A short list of named facts, read once: the name on the left, what it is on the right.</summary>
/// <remarks>Flux has no description list; this is Flux's text laid out as one, a column for the names and the rest for the values.</remarks>
internal sealed partial class DashboardFacts : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        Dl.Class("grid grid-cols-[minmax(0,9rem)_minmax(0,1fr)] gap-x-6 gap-y-3")[Children ?? []];
}
