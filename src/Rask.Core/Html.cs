namespace Rask;

/// <summary>
///     Every HTML and SVG element, and the primitives markup is written with — <c>Html.Div</c>, <c>Html.Text</c>,
///     <c>Html.Outlet</c> — as static members, so <c>global using static Rask.Html;</c> makes them bare anywhere.
/// </summary>
/// <remarks>
///     <para>
///         A component already has them: it inherits every entry from <c>RaskMarkup</c>, and that has not changed.
///         This class is for everything else — a test, a helper, a static factory of demo components — which with the
///         <c>using static</c> every template carries writes <c>Div["…"]</c> exactly as a component does.
///     </para>
///     <para>
///         It is also the qualified form of a tag. When a component has a member of a tag's name — a <c>Footer</c>
///         property, a <c>Label</c> — the member wins inside it, and <c>Html.Footer</c> still reaches the element.
///     </para>
///     <code>
///     static class Empty
///     {
///         public static Component State(string what) => Div.Class("empty")[P[$"No {what} yet"]];
///     }
///
///     Html.Footer["© 2026"]   // inside a component with a Footer property of its own
///     </code>
///     <para>
///         The <c>&lt;html&gt;</c> element itself is <c>Document</c>, since this class has its name. Filled in by the
///         builder generator from the same entries <c>RaskMarkup</c> carries, so the two cannot disagree.
///     </para>
/// </remarks>
public static partial class Html;
