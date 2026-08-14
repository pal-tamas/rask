namespace Rask.Core;

/// <summary>
///     A component under construction: what an entry hands back, and what every chain step takes and
///     returns until the markup ends.
/// </summary>
/// <remarks>
///     <para>
///         This type exists so a component's callbacks can be declared as ORDINARY DELEGATES —
///         <c>Action?</c>, <c>Func&lt;T, Component&gt;?</c> — and still be settable by a chain step of
///         the same name. C# resolves <c>x.OnClick(handler)</c> by looking <c>OnClick</c> up on the
///         receiver's type; when that lookup finds a property of delegate type it stops there and reads
///         the call as a delegate INVOCATION (CS1593). Extension methods are never considered once a
///         property has been found, so while the receiver was the component itself, every callback
///         property needed a non-invocable wrapper around its delegate to stay out of the lookup's way.
///     </para>
///     <para>
///         Moving the receiver one step off the component dissolves that. <c>Build&lt;T&gt;</c> declares
///         no <c>OnClick</c>, so the lookup finds nothing, extension methods come into play, and the
///         generated setter binds — whatever the property's type. A component author writes the delegate
///         they mean and nothing else.
///     </para>
///     <para>
///         It stays out of the way otherwise: the implicit conversion to <typeparamref name="T" /> is what
///         lets markup read exactly as it did — <c>Div.Class("card")[Span["hi"]]</c> — and lets a chain be
///         returned from <c>Render()</c>, passed to a <c>Component</c> parameter (through
///         <typeparamref name="T" />), or nested as a child with no cast.
///     </para>
///     <para>
///         The chain is meant to be the ONE way a component's properties are set, so that what a component
///         was given is the sequence of steps at its call site and nothing else. It is a CONVENTION, not a
///         guarantee: <see cref="Value" /> is hidden from completion rather than removed, and the implicit
///         conversion hands back the concrete component anyway, so
///         <c>Div d = Div.Class("card"); d.Id = "x";</c> compiles. Forbidding that needs an analyzer —
///         nothing in the type system can, once a chain converts to what it built.
///     </para>
///     <para>
///         A <c>readonly struct</c> over one reference: the chain allocates nothing beyond the component
///         it is building, which is what holds the surface at allocation parity with the factory it
///         replaced.
///     </para>
/// </remarks>
/// <typeparam name="T">The component being built.</typeparam>
public readonly struct Build<T>
    where T : Component
{
    /// <summary>Starts a chain over an already-constructed component.</summary>
    /// <remarks>
    ///     Called by the generated entries, which construct through <c>BuilderRuntime</c> so the component
    ///     keeps its positional identity across renders. Public because those entries are emitted into
    ///     consuming assemblies.
    /// </remarks>
    public Build(T component) => Value = component;

    /// <summary>The component this chain is building. Machinery — write a chain step instead.</summary>
    /// <remarks>
    ///     Public because the generated setters are emitted into consuming assemblies and have to reach
    ///     it; hidden from completion because a component is meant to be given everything by its chain.
    ///     Reading it back is what a test does to assert on what a chain produced.
    /// </remarks>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public T Value { get; }

    /// <summary>Ends the chain as the component it built.</summary>
    /// <remarks>
    ///     To <typeparamref name="T" /> rather than to <see cref="Component" />, and one operator covers
    ///     both: a user-defined conversion may be followed by a standard one, so a chain reaches
    ///     <c>Component</c> through <typeparamref name="T" /> anyway. Converting to the CONCRETE type is
    ///     what keeps the chain out of the way at a call site that wants the component itself — a
    ///     property typed as a particular component (<c>NativeBarItem? Leading</c>), a strongly-typed
    ///     children collection, a local the test then asserts against. Without it every one of those
    ///     needed an explicit hop through <see cref="Value" />, which is exactly the framework
    ///     bookkeeping the chain exists to remove.
    /// </remarks>
    public static implicit operator T(Build<T> chain) => chain.Value;

    /// <summary>Renders the finished markup to HTML.</summary>
    /// <remarks>
    ///     Forwarded rather than left to the implicit conversion: an extension method's receiver only
    ///     takes identity, reference and boxing conversions, and an instance method needs the instance —
    ///     so a <c>Component</c> member is not reachable from a chain without a cast. This one is
    ///     forwarded because rendering a chain is the ordinary thing to do with it; the framework's
    ///     internals are not, and stay behind the conversion.
    /// </remarks>
    public string ToHtml() => Value.ToHtml();

    /// <summary>Gives the component its children, ending the chain.</summary>
    /// <remarks>
    ///     Hands back <see cref="Component" /> rather than the chain, matching the indexer it forwards to:
    ///     children come last, and a <c>Component</c> result is what lets one arm of a conditional pair
    ///     with <c>null</c> or with different markup.
    /// </remarks>
    public Component this[params Component?[] children] => Value[children];

    /// <summary>Gives the component a pre-built sequence of children, ending the chain.</summary>
    public Component this[IEnumerable<Component?> children] => Value[children];
}
