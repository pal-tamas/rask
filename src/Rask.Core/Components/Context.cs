using Rask.Core.Live;

namespace Rask.Core.Components;

/// <summary>
///     Context propagation — supply a value to an entire descendant subtree without threading
///     it through the intermediate components (Rask's analogue of React Context / Blazor
///     CascadingValue). One type hosts both sides, the way a React context object carries both
///     its <c>.Provider</c> and the value <c>useContext</c> reads.
///     <para>Provide with <see cref="Provide{TValue}" />; consume with the static readers:</para>
///     <code>
///     // provide (inside a Render)
///     Context.Provide&lt;Theme&gt;(Value: dark)[ Sidebar(), Content() ]
/// 
///     // consume (inside any descendant's Render)
///     var theme = Context.Required&lt;Theme&gt;();   // throws if no provider
///     var maybe = Context.Get&lt;Theme&gt;();        // null if no provider
///     if (Context.Has&lt;Theme&gt;()) { ... }
///     </code>
/// </summary>
/// <remarks>
///     The provider node is transparent (like <see cref="Fragment" />) and special-cased in
///     <see cref="HtmlSerializer" />, which pushes <see cref="Value" /> onto the ambient
///     <see cref="ContextStack" /> for the duration of the children walk. Reading a value marks
///     the calling component as a context consumer, which opts it out of the render cache so a
///     later change to the provided value re-runs its <see cref="Component.Render" />.
///     <para>
///         The instance members (<see cref="Value" />/<see cref="ValueType" />/<see cref="Name" />)
///         are the erased provider state, set by the generic <c>Context&lt;TValue&gt;</c> factory;
///         the class is <c>[SkipFactory]</c> so the generator does not emit a factory over them.
///     </para>
/// </remarks>
[SkipFactory]
public sealed class Context : Component
{
    /// <summary>The provided value (boxed when a value type). May be null for reference types.</summary>
    public object? Value { get; set; }

    /// <summary>The declared type the value is provided as — the key consumers match against.</summary>
    public Type ValueType { get; set; } = typeof(object);

    /// <summary>
    ///     Optional disambiguator. When set, only reads passing the same name resolve to this
    ///     provider, letting two providers of the same type coexist.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    ///     Provide <paramref name="Value" /> of type <typeparamref name="TValue" /> to the whole
    ///     descendant subtree. Attach the subtree with the children indexer:
    ///     <c>Context.Provide&lt;Theme&gt;(Value: dark)[ … ]</c>. The runtime stays type-erased
    ///     (only <c>typeof(TValue)</c> plus a boxing assignment — no reflection over members), so
    ///     it is trim-safe. PascalCase parameter names match the generated-factory call style.
    /// </summary>
    public static Context Provide<TValue>(TValue Value, string? Name = null, object? Key = null) =>
        new() { Value = Value, ValueType = typeof(TValue), Name = Name, Key = Key };

    /// <summary>
    ///     The nearest provided value of type <typeparamref name="T" /> (optionally matched by
    ///     <paramref name="name" />), or <c>default</c> when none is in scope. A provider that
    ///     explicitly supplies a null reference still resolves (returns null), distinct from
    ///     "no provider".
    /// </summary>
    public static T? Get<T>(string? name = null)
    {
        MarkConsumer();
        return ContextStack.TryGet(typeof(T), name, out var value) ? (T?)value : default;
    }

    /// <summary>
    ///     The nearest provided value of type <typeparamref name="T" /> (optionally by
    ///     <paramref name="name" />), throwing <see cref="InvalidOperationException" /> when no
    ///     enclosing provider exists.
    /// </summary>
    public static T Required<T>(string? name = null)
    {
        MarkConsumer();
        if (ContextStack.TryGet(typeof(T), name, out var value))
        {
            return (T)value!;
        }

        var named = name is null ? "" : $" named '{name}'";
        throw new InvalidOperationException(
            $"No context value of type '{typeof(T)}'{named} is available. " +
            $"Wrap an ancestor in Context<{typeof(T).Name}>(value)[ … ] to provide one.");
    }

    /// <summary>
    ///     <c>true</c> when a value of type <typeparamref name="T" /> (optionally by
    ///     <paramref name="name" />) is provided by an enclosing <see cref="Context" />. Like
    ///     <see cref="Get{T}" />, marks the caller a context consumer so it re-renders when an
    ///     ancestor begins/stops providing the value — otherwise a component that gates purely on
    ///     <c>Has</c> would be render-cached and show stale UI when the provider appears or leaves.
    /// </summary>
    public static bool Has<T>(string? name = null)
    {
        MarkConsumer();
        return ContextStack.TryGet(typeof(T), name, out _);
    }

    private static void MarkConsumer() => LiveRenderContext.Current?.MarkCurrentConsumesContext();
}
