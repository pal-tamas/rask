namespace Rask;

/// <summary>
///     Every HTML and SVG element and markup primitive — <c>Div</c>, <c>Text</c>, <c>Outlet</c> — as a static member,
///     so they are bare in any class, not only in a component (which inherits them).
/// </summary>
/// <remarks>
///     The qualified form reaches a tag your own member hides: <c>Markup.Footer["© 2026"]</c> inside a component with a
///     <c>Footer</c> property. Filled in by the builder generator from the entries every component inherits.
/// </remarks>
public static partial class Markup;
