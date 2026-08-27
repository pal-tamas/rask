namespace Rask.Core.Rendering;

/// <summary>
///     How far up the render ladder a page should go, when the automatic answer is wrong.
/// </summary>
public enum RenderMode
{
    /// <summary>
    ///     Decided from what the render did — a handler, a form, an element <c>Ref</c>, a call into
    ///     JavaScript, async work still in flight. The default, and right for almost every page.
    /// </summary>
    Auto = 0,

    /// <summary>
    ///     Serve this page as a plain document even though the app allows more.
    /// </summary>
    /// <remarks>
    ///     A request rather than a command: if the render shows the page genuinely needs a
    ///     connection, it keeps one and the contradiction is reported. Declaring a page static and
    ///     having its buttons silently stop working is the one outcome worth refusing.
    /// </remarks>
    Static,

    /// <summary>
    ///     Keep a live connection for this page even though its render showed no need for one.
    /// </summary>
    /// <remarks>
    ///     The escape hatch for what detection cannot see: a component that pushes from a timer or
    ///     an <c>event</c> subscription does nothing during the walk, so nothing marks it. Declare it
    ///     on that component and every page using it inherits the need.
    /// </remarks>
    Interactive,
}

/// <summary>
///     Overrides the automatic render decision for a page, or for any component a page uses.
/// </summary>
/// <remarks>
///     <para>
///         Nothing needs this. How far a page climbs is detected from its render, and the detection
///         is deliberately biased towards keeping a connection — a page wrongly judged interactive
///         behaves exactly as it always has, while one wrongly judged static loses its interactivity
///         silently. This attribute exists for the cases detection cannot see.
///     </para>
///     <para>
///         <see cref="RenderMode.Interactive" /> is honoured from <b>anywhere</b> in the page's
///         tree, which is what lets a base component declare the need once — a polling panel says it,
///         and every dashboard built on it inherits it without its author knowing to.
///     </para>
///     <para>
///         <see cref="RenderMode.Static" /> is honoured only on the page itself or the app root.
///         Letting an arbitrary helper deep in a tree force a whole page static would be a very
///         quiet way to break it.
///     </para>
///     <para>
///         It can only move a page <b>within</b> what <c>RenderModes</c> allows. A page cannot ask
///         for a rung the app has turned off.
///     </para>
/// </remarks>
/// <param name="mode">How far this page, or any page using this component, should go.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class RenderModeAttribute(RenderMode mode) : Attribute
{
    /// <summary>The declared mode.</summary>
    public RenderMode Mode { get; } = mode;
}
