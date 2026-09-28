namespace Rask.Core.Components;

/// <summary>
///     The doctype the framework itself puts in front of the document it composes, for the render where the
///     root boundary owns the page. Users write <c>Doctype</c>; this is the same declaration under a name
///     the root path can reach without going through an entry the user could shadow.
/// </summary>
/// <remarks>
///     A separate type rather than a <c>new DoctypeComponent()</c> because the root path needs a builder
///     ENTRY, not an allocation: an entry resolves through <c>LiveRenderContext.GetOrCreateEntry</c>, which is
///     what gives the node a stable identity across renders. A bare <c>new</c> renders identical HTML and then
///     silently defeats the render cache in a live session — on the one component that re-renders every frame.
/// </remarks>
internal sealed class CoreDoctype : DoctypeComponent;
