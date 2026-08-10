namespace Rask.Core;

/// <summary>
///     PROTOTYPE — the runtime half of the builder surface's entry points. The entries themselves
///     are generated (see <c>RaskBuilderEntries.g.cs</c>); this is the helper they all call.
/// </summary>
/// <remarks>
///     <para>
///         Each generated entry is a <c>protected static</c> property whose name IS its component
///         type, so <c>Div[…]</c> reads as a value while <c>Div</c> stays usable as a type (C#'s
///         "Color Color" rule, §12.8.7.2). They must be <em>inherited</em> rather than imported: a
///         <c>using static</c> property loses to a same-named type in scope (CS0119), while a member
///         of the enclosing type wins. That is what removes every global using.
///     </para>
///     <para>
///         Coexists with the generated <c>Generated.Div(…)</c> factories: an entry property is not
///         invocable, so <c>Div()</c> still binds to the factory method via C#'s invocable-member
///         rule. Both surfaces work in the same file during migration.
///     </para>
/// </remarks>
public abstract partial class Component
{
    /// <summary>
    ///     Builds an entry's instance the same way the generated factory does.
    /// </summary>
    /// <remarks>
    ///     Routing through <c>LiveRenderContext.GetOrCreateEntry</c> is not optional: identity is
    ///     positional per (parent, type), and it is what makes the render cache and reconciliation reuse
    ///     an instance across renders. A bare <c>new()</c> here compiles and renders identical HTML in a
    ///     detached <c>ToHtml()</c> tree (which has no context), then silently defeats the cache in a
    ///     live session. <c>protected</c> rather than private because the generator injects entries for
    ///     a consumer's own components into that consumer's partial class, in another assembly.
    ///     <para>
    ///         It also arms the parent's deferred commit. An entry cannot call <c>NotifyParameters</c>
    ///         the way a factory does — the props arrive afterwards, one setter at a time, and the chain
    ///         has no natural end — so lifecycle and <c>PropsDirty</c> are fired for it when the parent's
    ///         <c>Render()</c> returns and the chain is provably finished.
    ///     </para>
    /// </remarks>
    /// <remarks>
    ///     <para>
    ///         <paramref name="reset" /> puts the component's non-folding props (raw delegates, carriers,
    ///         <c>Key</c>) back to the value the factory would pass for an omitted parameter, and
    ///         <paramref name="pendingReset" /> is deferred to the end of the parent's <c>Render()</c>
    ///         for the folding ones — see <see cref="BuilderRuntime" /> for why the two halves cannot
    ///         share a moment. Outside a render context every entry is a fresh instance, so there is
    ///         nothing stale to reset.
    ///     </para>
    /// </remarks>
    protected static T Entry<T>(
        Action<Component> reset,
        Action<Component, ulong> pendingReset,
        ulong pending,
        bool hasLifecycle = true)
        where T : Component, new()
        => BuilderRuntime.Entry<T>(reset, pendingReset, pending, hasLifecycle);

    /// <summary>
    ///     The entry for a component whose only constructor takes injected services.
    /// </summary>
    /// <remarks>
    ///     Mirrors the generated factory's DI branch: construction goes through
    ///     <c>ActivatorUtilities</c> inside <see cref="Live.LiveRenderContext.GetOrCreate{T}" />. Outside a
    ///     render context there is no service provider to construct from, so this throws with the same
    ///     message the factory uses rather than returning a half-built component.
    /// </remarks>
    protected static T EntryDi<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
        System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)] T>(
        Action<Component> reset,
        Action<Component, ulong> pendingReset,
        ulong pending,
        bool hasLifecycle = true)
        where T : Component
        => BuilderRuntime.EntryDi<T>(reset, pendingReset, pending, hasLifecycle);

    // A generic component's entry used to have a helper of its own — EntryBound<TControl, TValue>, which
    // took the Bind expression and assigned it. It is gone because the generated entry now assigns its
    // own inference property inline, which is what lets ONE emission serve a bound form control and any
    // other generic component: the helper could only ever assign `Bind`, so a second shape would have
    // needed a second helper, and two helpers is how the eligibility rules drifted apart before.

    /// <summary>
    ///     The entry for a component that declares a <c>required</c> member.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <see cref="Entry{T}" /> in every respect but one: it does not carry the <c>new()</c>
    ///         constraint, because a type with a required member does not satisfy it (CS9040). That single
    ///         constraint used to withhold an entry outright from <c>BsToast</c>, <c>BsStat</c> and
    ///         <c>FluentValidationValidator</c> — components that would simply cease to exist the day the
    ///         factory is deleted.
    ///     </para>
    ///     <para>
    ///         Requiredness is a compile-time check with no runtime enforcement, so
    ///         <see cref="Activator.CreateInstance{T}" /> is allowed to build what <c>new T()</c> may not.
    ///         What enforces the value afterwards is RASK038 on the chain — the same trade the builder
    ///         surface already makes for a RASK001-required property, and the reason the two land together.
    ///     </para>
    /// </remarks>
    // The annotation the trimmer needs to keep the parameterless constructor of every type that flows
    // through here; without it the WASM publish reports IL2091.
    protected static T EntryRequired<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
        System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>(
        Action<Component> reset,
        Action<Component, ulong> pendingReset,
        ulong pending,
        bool hasLifecycle = true)
        where T : Component
        => BuilderRuntime.EntryRequired<T>(reset, pendingReset, pending, hasLifecycle);
}
