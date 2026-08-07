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
///         the change is invisible at every site except the prop's declared type.
///     </para>
///     <para>
///         Deliberately does NOT apply <see cref="AutoCallback" />: whether a callback re-renders its
///         owner depends on the component, not the carrier. Element handlers go straight to the DOM
///         (owner resolution already re-renders, and wrapping would allocate per render); non-Element
///         component callbacks are wrapped by the setter. Putting the decision in the conversion would
///         silently regress the render hot path.
///     </para>
/// </remarks>
public readonly record struct Handler(Callback? Fn)
{
    public void Invoke() => Fn?.Invoke();

    public static implicit operator Handler(Callback? fn) => new(fn);
}

/// <summary>The async sibling of <see cref="Handler" />.</summary>
public readonly record struct HandlerAsync(CallbackAsync? Fn)
{
    public Task InvokeAsync() => Fn?.Invoke() ?? Task.CompletedTask;

    public static implicit operator HandlerAsync(CallbackAsync? fn) => new(fn);
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
///         The implicit conversion keeps plain assignment (<c>Validate = rule</c>) and the generated
///         factories' <c>Validate:</c> parameter working unchanged — the generator maps a carrier prop
///         back to its delegate for every parameter it emits, so no call site sees the carrier.
///     </para>
///     <para>
///         Nullable at the use site (<c>Carrier&lt;…&gt;?</c>), never bare: a non-nullable struct with
///         no initializer is a <em>required</em> factory parameter (RASK001 / CS9040).
///     </para>
/// </remarks>
public readonly record struct Carrier<TDelegate>(TDelegate? Fn) where TDelegate : Delegate
{
    public static implicit operator Carrier<TDelegate>(TDelegate? fn) => new(fn);
}
