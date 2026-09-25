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
