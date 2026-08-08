namespace Rask.Core;

/// <summary>
///     PROTOTYPE — a non-delegate carrier for a component's callback prop, so the prop and its
///     builder setter can share a name.
/// </summary>
/// <remarks>
///     <para>
///         A delegate-typed member <em>is</em> invocable, so <c>c.OnSelect(handler)</c> binds to the
///         delegate and fails with CS1593 — which is why a raw-delegate prop forces the setter to be
///         renamed (<c>WhenSelect</c>) or the prop to drop its prefix. Wrapping the delegate in a
///         struct makes the member non-invocable, so C#'s invocable-member rule skips it and the
///         same-named setter binds, exactly as it already does for <c>string</c>/<c>int</c> props.
///     </para>
///     <para>
///         The implicit conversion keeps ordinary assignment working (<c>OnSelect = Choose</c>), so
///         the change is invisible at every site except the prop's declared type. Calling the callback
///         back is <c>OnSelect?.Invoke()</c> — the carrier's public surface is the invocation, not the
///         delegate it holds. The delegate itself (<c>Fn</c>) is deliberately internal: handing it out
///         re-exposes the null trap the carrier exists to close (see <see cref="From" />) and invites
///         call sites to re-wrap a delegate the framework already owns. The <em>declaring</em> struct is
///         written without a positional parameter for that reason — a positional record would publish
///         <c>Fn</c> as a property no matter what.
///     </para>
///     <para>
///         <see cref="Element" />'s whole GlobalEventHandlers surface is declared with these carriers —
///         which is what makes the DOM setter <c>.OnClick(…)</c> rather than <c>.Click(…)</c>. There the
///         carrier is a pure view: the handler still lives in the element's event dictionary as the raw
///         delegate, so nothing about registration or dispatch changes.
///     </para>
///     <para>
///         Deliberately does NOT apply <see cref="AutoCallback" />: whether a callback re-renders its
///         owner depends on the component, not the carrier. Element handlers go straight to the DOM
///         (owner resolution already re-renders, and wrapping would allocate per render); non-Element
///         component callbacks are wrapped by the setter. Putting the decision in the conversion would
///         silently regress the render hot path.
///     </para>
/// </remarks>
public readonly record struct Handler
{
    /// <summary>Wraps <paramref name="fn" />. Prefer <see cref="From" />, which maps null to unset.</summary>
    public Handler(Callback? fn) => Fn = fn;

    // The carried delegate. INTERNAL: Invoke is the public read surface (see the type remarks); the
    // framework itself still needs the delegate — Element's facade setters store it raw in the DOM event
    // dictionary, and the wrap-preservation tests assert its identity rather than invoking it.
    internal Callback? Fn { get; }

    /// <summary>Runs the carried callback; a no-op when nothing is wired.</summary>
    public void Invoke() => Fn?.Invoke();

    /// <inheritdoc cref="Carrier{TDelegate}.From" />
    public static Handler? From(Callback? fn) => fn is null ? (Handler?)null : new Handler(fn);

    public static implicit operator Handler(Callback? fn) => new(fn);
}

/// <summary>The async sibling of <see cref="Handler" />.</summary>
public readonly record struct HandlerAsync
{
    /// <summary>Wraps <paramref name="fn" />. Prefer <see cref="From" />, which maps null to unset.</summary>
    public HandlerAsync(CallbackAsync? fn) => Fn = fn;

    // The carried delegate — internal for the reason Handler.Fn is.
    internal CallbackAsync? Fn { get; }

    /// <summary>Runs the carried callback; completes synchronously when nothing is wired.</summary>
    public Task InvokeAsync() => Fn?.Invoke() ?? Task.CompletedTask;

    /// <inheritdoc cref="Carrier{TDelegate}.From" />
    public static HandlerAsync? From(CallbackAsync? fn) =>
        fn is null ? (HandlerAsync?)null : new HandlerAsync(fn);

    public static implicit operator HandlerAsync(CallbackAsync? fn) => new(fn);
}

/// <summary>The argument-taking <see cref="Handler" />: carries a <see cref="Callback{T}" />.</summary>
/// <remarks>
///     Completes the pair the way <see cref="Callback{T}" /> completes <see cref="Callback" />, so an
///     event that carries arguments reads as <c>Handler&lt;MouseEventArgs&gt;?</c> rather than
///     <c>Carrier&lt;Callback&lt;MouseEventArgs&gt;&gt;?</c>. <see cref="Element" />'s whole
///     GlobalEventHandlers surface is declared with these four carriers.
/// </remarks>
public readonly record struct Handler<TArgs>
{
    /// <summary>Wraps <paramref name="fn" />. Prefer <see cref="From" />, which maps null to unset.</summary>
    public Handler(Callback<TArgs>? fn) => Fn = fn;

    // The carried delegate — internal for the reason Handler.Fn is.
    internal Callback<TArgs>? Fn { get; }

    /// <summary>Runs the carried callback with <paramref name="args" />; a no-op when nothing is wired.</summary>
    public void Invoke(TArgs args) => Fn?.Invoke(args);

    /// <inheritdoc cref="Carrier{TDelegate}.From" />
    public static Handler<TArgs>? From(Callback<TArgs>? fn) =>
        fn is null ? (Handler<TArgs>?)null : new Handler<TArgs>(fn);

    public static implicit operator Handler<TArgs>(Callback<TArgs>? fn) => new(fn);
}

/// <summary>The async sibling of <see cref="Handler{TArgs}" />.</summary>
public readonly record struct HandlerAsync<TArgs>
{
    /// <summary>Wraps <paramref name="fn" />. Prefer <see cref="From" />, which maps null to unset.</summary>
    public HandlerAsync(CallbackAsync<TArgs>? fn) => Fn = fn;

    // The carried delegate — internal for the reason Handler.Fn is.
    internal CallbackAsync<TArgs>? Fn { get; }

    /// <summary>
    ///     Runs the carried callback with <paramref name="args" />; completes synchronously when nothing
    ///     is wired.
    /// </summary>
    public Task InvokeAsync(TArgs args) => Fn?.Invoke(args) ?? Task.CompletedTask;

    /// <inheritdoc cref="Carrier{TDelegate}.From" />
    public static HandlerAsync<TArgs>? From(CallbackAsync<TArgs>? fn) =>
        fn is null ? (HandlerAsync<TArgs>?)null : new HandlerAsync<TArgs>(fn);

    public static implicit operator HandlerAsync<TArgs>(CallbackAsync<TArgs>? fn) => new(fn);
}

/// <summary>
///     The open-ended carrier: <see cref="Handler" /> for an arbitrary delegate type.
/// </summary>
/// <remarks>
///     <para>
///         Same job as <see cref="Handler" /> — make a delegate-typed prop non-invocable so its
///         builder setter can share its name — but parameterised by the delegate, so one type covers
///         every shape. <c>IFormControl&lt;T&gt;</c>'s bound members use it
///         (<c>Carrier&lt;Validate&lt;T&gt;&gt;? Validate</c>), which is what makes
///         <c>Input(() =&gt; m.Name).Validate(rule)</c> bind to the setter instead of trying to invoke
///         the validator with a validator.
///     </para>
///     <para>
///         Its <see cref="Fn" /> is the one carrier delegate that stays PUBLIC — the four
///         <see cref="Handler" /> shapes keep theirs internal behind <c>Invoke</c>. There is no
///         <c>Invoke</c> to offer here: the delegate is a type parameter, so its signature (arity,
///         argument types, return type) is unknown to this type and no method can stand in for calling
///         it. A component that declares a value-returning callback prop —
///         <c>Carrier&lt;Func&lt;T, string?&gt;&gt;? RowClass</c> — has to reach the delegate to use it,
///         and it is usually in another assembly. Reading it back is the deliberate escape hatch;
///         <em>constructing</em> one still goes through <see cref="From" />.
///     </para>
///     <para>
///         The implicit conversion keeps plain assignment (<c>Validate = rule</c>) and the generated
///         factories' <c>Validate:</c> parameter working unchanged — the generator maps a carrier prop
///         back to its delegate for every parameter it emits, so no call site sees the carrier.
///     </para>
///     <para>
///         Nullable at the use site (<c>Carrier&lt;…&gt;?</c>), never bare: a non-nullable struct with
///         no initializer is a <em>required</em> factory parameter (RASK001 / CS9040).
///     </para>
/// </remarks>
public readonly record struct Carrier<TDelegate> where TDelegate : Delegate
{
    /// <summary>Wraps <paramref name="fn" />. Prefer <see cref="From" />, which maps null to unset.</summary>
    public Carrier(TDelegate? fn) => Fn = fn;

    /// <summary>The carried delegate, or <c>null</c> when the carrier is unset.</summary>
    public TDelegate? Fn { get; }

    /// <summary>
    ///     Wraps a delegate, mapping <c>null</c> to an <em>unset</em> carrier — the null-preserving
    ///     counterpart of the implicit conversion.
    /// </summary>
    /// <remarks>
    ///     The implicit conversion accepts a null delegate, so converting one yields a non-null carrier
    ///     wrapping null: an omitted handler that no longer reads back as unset, silently flipping every
    ///     <c>OnClose is not null</c> test a component makes about its own callback. Every generated
    ///     assignment to a carrier property goes through <c>From</c> for that reason; hand-written code
    ///     that needs the same guarantee should too (or cast the unset branch — <c>(Handler?)null</c>).
    ///     Both forms are allocation-free: the carrier is a struct and <see cref="Nullable{T}" /> of one
    ///     stays on the stack.
    ///     <para>
    ///         The cast inside <c>From</c> itself is load-bearing for the same reason it is at a call
    ///         site: without it the conditional's natural type is the bare carrier, so the null branch
    ///         runs the conversion and <c>From</c> hands back exactly what it exists to prevent.
    ///     </para>
    /// </remarks>
    public static Carrier<TDelegate>? From(TDelegate? fn) =>
        fn is null ? (Carrier<TDelegate>?)null : new Carrier<TDelegate>(fn);

    public static implicit operator Carrier<TDelegate>(TDelegate? fn) => new(fn);
}
