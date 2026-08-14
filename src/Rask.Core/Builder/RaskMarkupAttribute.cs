namespace Rask.Core;

/// <summary>
///     PROTOTYPE — the builder surface, without spending the base slot. Put this on a
///     <c>partial</c> type and it becomes a markup host: <c>Div</c>, <c>Span</c>, your own components
///     and any referenced library's are all in scope inside it, exactly as they are inside a component.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="RaskMarkup" /> is the cheaper way to say the same thing and should be preferred
///         whenever the base slot is free. This exists for the two shapes where it is not available at
///         all: a type whose base belongs to someone else (a fixture base from a test library, a
///         framework's <c>TestBed&lt;T&gt;</c>), and a <c>static class</c>, which can derive from
///         nothing.
///     </para>
///     <para>
///         The two compose rather than competing. When the attributed type's base slot is still free the
///         generator writes <c>: RaskMarkup</c> into its own generated <c>partial</c> — same inheritance,
///         same cost, you simply did not have to type it. Only when the slot is taken (or the type is
///         <c>static</c>) does it fall back to injecting the framework entries as members, which is
///         several times as much generated source per host. So the attribute is always correct and never
///         pays for what it does not need.
///     </para>
///     <para>
///         <b>Direct, not inherited.</b> A subclass of an attributed type is not itself a host: it
///         already has whatever the base has, and making it one would demand <c>partial</c> (RASK036) of
///         every subclass of a shared base, in files that name no markup. Opting in is a declaration, so
///         it stays with the declaration that made it.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RaskMarkupAttribute : Attribute;
