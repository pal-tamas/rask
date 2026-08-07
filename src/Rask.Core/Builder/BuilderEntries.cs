using Rask.Core.Live;

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
    ///     Routing through <see cref="LiveRenderContext.GetOrCreate{T}" /> is not optional: identity is
    ///     positional per (parent, type), and it is what makes the render cache and reconciliation reuse
    ///     an instance across renders. A bare <c>new()</c> here compiles and renders identical HTML in a
    ///     detached <c>ToHtml()</c> tree (which has no context), then silently defeats the cache in a
    ///     live session. <c>protected</c> rather than private because the generator injects entries for
    ///     a consumer's own components into that consumer's partial class, in another assembly.
    /// </remarks>
    protected static T Entry<T>() where T : Component, new() =>
        LiveRenderContext.Current is { } ctx ? ctx.GetOrCreate<T>(static _ => new T()) : new T();

    /// <summary>
    ///     The entry for a component whose only constructor takes injected services.
    /// </summary>
    /// <remarks>
    ///     Mirrors the generated factory's DI branch: construction goes through
    ///     <c>ActivatorUtilities</c> inside <see cref="LiveRenderContext.GetOrCreate{T}" />. Outside a
    ///     render context there is no service provider to construct from, so this throws with the same
    ///     message the factory uses rather than returning a half-built component.
    /// </remarks>
    protected static T EntryDi<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
        System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : Component
    {
        if (LiveRenderContext.Current is { } ctx)
        {
            return ctx.GetOrCreate<T>(static sp =>
                Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<T>(sp));
        }

        throw new InvalidOperationException(
            $"Component '{typeof(T)}' has no parameterless constructor; it can only be instantiated "
            + "inside a LiveRenderContext (e.g. via MapRask<TApp>).");
    }

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
        System.Linq.Expressions.Expression<Func<TValue>> bind)
        where TControl : Component, Forms.IFormControl<TValue>, new()
    {
        var control = Entry<TControl>();
        control.Bind = bind;
        return control;
    }
}
