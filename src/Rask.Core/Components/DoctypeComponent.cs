namespace Rask.Core.Components;

/// <summary>
///     The serializer's hook for the document type declaration. <c>Doctype</c> itself is an HTML component and
///     lives in <c>Rask.Html</c> with the rest of the tag family; this base is what stays behind so
///     <see cref="HtmlSerializer" /> can recognise it without Core depending on the assembly downstream of it.
/// </summary>
/// <remarks>
///     An abstract base rather than a marker interface on purpose: the serializer's dispatch is a linear type
///     switch over sealed component shapes, and a class check stays a single cast where an interface check
///     would walk the interface map on a path that runs for every component of every render.
/// </remarks>
public abstract class DoctypeComponent : Component;

/// <summary>
///     The doctype the framework itself puts in front of the document it composes, for the render where the
///     root boundary owns the page. Users write <c>Doctype</c> (Rask.Html); this is the same declaration
///     under a name Core can reach.
/// </summary>
/// <remarks>
///     A separate type rather than a <c>new DoctypeComponent()</c> because the root path needs a builder
///     ENTRY, not an allocation: an entry resolves through <c>LiveRenderContext.GetOrCreateEntry</c>, which is
///     what gives the node a stable identity across renders. A bare <c>new</c> renders identical HTML and then
///     silently defeats the render cache in a live session — on the one component that re-renders every frame.
/// </remarks>
internal sealed class CoreDoctype : DoctypeComponent;
