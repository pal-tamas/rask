namespace Rask.Core.Components;

/// <summary>
///     The serializer's hook for the document type declaration. <c>Doctype</c> itself is an ordinary tag
///     component; this base is what <see cref="HtmlSerializer" /> matches on, so the declaration is
///     recognised by SHAPE rather than by naming one concrete type.
/// </summary>
/// <remarks>
///     An abstract base rather than a marker interface on purpose: the serializer's dispatch is a linear type
///     switch over sealed component shapes, and a class check stays a single cast where an interface check
///     would walk the interface map on a path that runs for every component of every render.
/// </remarks>
public abstract class DoctypeComponent : Component;

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
