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
        ulong pending)
        where T : Component, new()
        => BuilderRuntime.Entry<T>(reset, pendingReset, pending);

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
        ulong pending)
        where T : Component
        => BuilderRuntime.EntryDi<T>(reset, pendingReset, pending);

    /// <summary>
    ///     The entry for a generic <see cref="Forms.IFormControl{T}" /> in bound mode.
    /// </summary>
    /// <remarks>
    ///     A property cannot be generic, so a generic control's entry is a static method — and its one
    ///     argument is what infers the value type: <c>Input(() =&gt; model.Age)</c> yields
    ///     <c>Input&lt;int&gt;</c>. Bind is the only parameter; the validator and the post-bind hooks are
    ///     setters (<c>.Validate(…)</c> / <c>.AfterBind(…)</c>), which is what collapses the generated
    ///     factory's none/sync/async overload fan-out into one entry.
    /// </remarks>
    protected static TControl EntryBound<TControl, TValue>(
        System.Linq.Expressions.Expression<Func<TValue>> bind,
        Action<Component> reset,
        Action<Component, ulong> pendingReset,
        ulong pending)
        where TControl : Component, Forms.IFormControl<TValue>, new()
        => BuilderRuntime.EntryBound<TControl, TValue>(bind, reset, pendingReset, pending);
}
