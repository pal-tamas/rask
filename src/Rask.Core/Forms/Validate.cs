namespace Rask.Core.Forms;

/// <summary>
///     A synchronous per-field/model validator: given the current value, returns zero or more error
///     messages (an empty sequence means valid). The shared shape every form control accepts as its
///     <c>Validate</c> parameter — <c>Input</c>/<c>Select</c>/<c>Textarea</c>/<c>Form</c> and the sample
///     <c>CheckboxGroup</c>/<c>RadioGroup</c>. See <c>docs/forms.md</c> §3 and §9.
/// </summary>
/// <remarks>
///     Contravariant in <typeparamref name="T" /> so a validator written against a base type can validate
///     a more-derived bound value. Registered with the <see cref="EditContext" /> and invoked via
///     <c>DelegateValidator</c>, which tells it from the asynchronous rule by its return type: the messages
///     themselves select the synchronous path.
/// </remarks>
public delegate IEnumerable<string> Validate<in T>(T value);
