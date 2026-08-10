namespace Rask.Core;

/// <summary>
///     PROTOTYPE — the builder surface, and nothing else. Derive from this to write markup somewhere
///     that is not a component: a test class, a fixture, a page-object, a factory of demo components.
/// </summary>
/// <remarks>
///     <para>
///         Every framework entry (<c>Div</c>, <c>Span</c>, <c>Input</c>, …) is a <c>protected static</c>
///         member emitted onto <i>this</i> class, and <see cref="Component" /> derives from it — so the
///         166 entries exist once, in one place, and a component and a test class reach the same ones by
///         the same rule. That is the whole reason the entries moved here rather than being emitted a
///         second time: two emissions of the same surface are two things free to drift.
///     </para>
///     <para>
///         Deliberately NOT a <see cref="Component" />. A test class does not want
///         <c>Render()</c>, the lifecycle hooks, a positional <c>GetOrCreate</c> identity or a render
///         cache; it wants to be able to <i>name</i> markup. This class has no members of its own at all
///         — no state, no virtuals, nothing to override — so deriving from it costs a base slot and
///         nothing else.
///     </para>
///     <para>
///         A consuming assembly's own components, and any referenced component library's, are not here:
///         a generator cannot add members to a type it does not declare. Those are injected into the
///         host's own <c>partial</c>, exactly as they are for a component — so a markup host that names
///         one must be <c>partial</c> (RASK036).
///     </para>
///     <para>
///         A <c>static class</c> cannot derive from anything, so it cannot be a markup host. It has two
///         ways to reach the surface anyway, both of which are ordinary C# rather than anything Rask
///         adds: stop being <c>static</c> (a sealed class with a private constructor holds static
///         members just as well, and a static field initializer, a delegate field and a lambda all reach
///         an inherited <c>protected static</c> member), or nest inside a markup host (simple-name
///         lookup walks out through enclosing types).
///     </para>
/// </remarks>
public abstract partial class RaskMarkup
{
    /// <summary>Only a derived type can construct one; nothing here has state to initialise.</summary>
    protected RaskMarkup()
    {
    }
}
