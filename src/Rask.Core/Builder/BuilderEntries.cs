using Rask.Core.Components;
using Rask.Core.Live;

namespace Rask.Core;

/// <summary>
///     PROTOTYPE — the builder surface's entry points.
/// </summary>
/// <remarks>
///     <para>
///         Each entry is a <c>protected static</c> property whose name IS its component type, so
///         <c>Div[…]</c> reads as a value while <c>Div</c> stays usable as a type (C#'s
///         "Color Color" rule, §12.8.7.2). They must be <em>inherited</em> rather than imported:
///         a <c>using static</c> property loses to a same-named type in scope (CS0119), while a
///         member of the enclosing type wins. That is what removes every global using.
///     </para>
///     <para>
///         Coexists with the generated <c>Generated.Div(…)</c> factories: an entry property is not
///         invocable, so <c>Div()</c> still binds to the factory method via C#'s invocable-member
///         rule. Both surfaces work in the same file during migration.
///     </para>
///     <para>
///         Deliberately excluded from this slice — each shadows a same-named property on a derived
///         component and needs a <c>new</c> modifier there before it can be added:
///         <c>Head</c> (hard conflict with <see cref="Head" />), <c>Style</c>, <c>Title</c>,
///         <c>Data</c> (Element), <c>Form</c>, <c>Template</c>, <c>Label</c>, <c>Cite</c>,
///         <c>Body</c>, <c>Span</c>, <c>Abbr</c>, <c>Audio</c>, <c>Video</c>, <c>Context</c>.
///     </para>
/// </remarks>
public abstract partial class Component
{
    // Entries MUST go through GetOrCreate, exactly as the generated factories do: identity is
    // positional per (parent, type), and it is what makes the render cache and reconciliation reuse
    // an instance across renders. A bare `new()` here compiles and renders the same HTML in a
    // detached ToHtml() tree (no LiveRenderContext), then silently defeats the cache in a live session.
    // `protected`, not private: the generator injects entries for a consumer's OWN components into
    // that consumer's partial class, in a different assembly, and those forwarders call this.
    protected static T Entry<T>() where T : Component, new() =>
        LiveRenderContext.Current is { } ctx ? ctx.GetOrCreate<T>(static _ => new T()) : new T();

    protected static A A => Entry<A>();
    protected static Article Article => Entry<Article>();
    protected static Aside Aside => Entry<Aside>();
    protected static Br Br => Entry<Br>();
    protected static Button Button => Entry<Button>();
    protected static Code Code => Entry<Code>();
    protected static Div Div => Entry<Div>();
    protected static Em Em => Entry<Em>();
    protected static Footer Footer => Entry<Footer>();
    protected static H1 H1 => Entry<H1>();
    protected static H2 H2 => Entry<H2>();
    protected static H3 H3 => Entry<H3>();
    protected static H4 H4 => Entry<H4>();
    protected static Header Header => Entry<Header>();
    protected static Hr Hr => Entry<Hr>();
    protected static Img Img => Entry<Img>();
    protected static Li Li => Entry<Li>();
    protected static Main Main => Entry<Main>();
    protected static Nav Nav => Entry<Nav>();
    protected static NavLink NavLink => Entry<NavLink>();
    protected static Ol Ol => Entry<Ol>();
    protected static P P => Entry<P>();
    protected static Pre Pre => Entry<Pre>();
    protected static Section Section => Entry<Section>();
    protected static Small Small => Entry<Small>();
    protected static Strong Strong => Entry<Strong>();
    protected static Table Table => Entry<Table>();
    protected static Tbody Tbody => Entry<Tbody>();
    protected static Td Td => Entry<Td>();
    protected static Tfoot Tfoot => Entry<Tfoot>();
    protected static Th Th => Entry<Th>();
    protected static Thead Thead => Entry<Thead>();
    protected static Tr Tr => Entry<Tr>();
    protected static Ul Ul => Entry<Ul>();
}
