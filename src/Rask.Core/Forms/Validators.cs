namespace Rask.Core.Forms;

/// <summary>
///     A synchronous per-field/model validator: given the current value, returns zero or more error
///     messages (an empty sequence means valid). The shared shape every form control accepts as its
///     <c>Validate</c> parameter — <c>Input</c>/<c>Select</c>/<c>Textarea</c>/<c>Form</c> and the sample
///     <c>CheckboxGroup</c>/<c>RadioGroup</c>/<c>MultiSelect</c>. See <c>docs/forms.md</c> §3 and §9.
/// </summary>
/// <remarks>
///     Contravariant in <typeparamref name="T" /> so a validator written against a base type can validate
///     a more-derived bound value. Registered with the <see cref="EditContext" /> and invoked via
///     <c>DelegateValidator</c> (which dispatches by arity), so the one-argument shape selects the
///     synchronous path.
/// </remarks>
public delegate IEnumerable<string> Validate<in T>(T value);

/// <summary>
///     The asynchronous counterpart of <see cref="Validate{T}" />: given the current value and a
///     cancellation token, returns the error messages once any async work (a server round-trip, say)
///     completes. Selected over the sync overload by the two-argument lambda shape at the call site.
/// </summary>
/// <remarks>
///     Latest-write-wins cancellation is driven by the <see cref="EditContext" />; honour the supplied
///     <see cref="CancellationToken" /> and let <see cref="OperationCanceledException" /> propagate so a
///     superseded check is dropped without surfacing a message.
/// </remarks>
public delegate ValueTask<IEnumerable<string>> ValidateAsync<in T>(T value, CancellationToken cancellationToken);
