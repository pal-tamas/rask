using System.Linq.Expressions;

namespace Rask.Core.Forms;

// The contract a custom form control implements so the factory generator can synthesize its
// factories — both a bound factory (`Control(() => model.Field, …)`, two-way binding + per-field
// validation, with the validator fanned into none/sync/async overloads) and a controlled factory
// (`Control(…, Value: v, OnChange: …)`, parent-owned state). A control declares the value type T
// once; the generator reads it off the interface and emits the typed surface.
//
// Members use fixed names (Bind/Validate/…/Value/OnChange/…) — the generator recognizes them by
// name and excludes the bound-mode members from the controlled factory (so no [SkipFactory] is
// needed). `Validate<T>`/`ValidateAsync<T>` (this namespace) and `Callback<T>`/`CallbackAsync<T>`
// (Rask.Core) are the framework's named delegate types; the generator collapses the sync/async
// validator pair into the none/sync/async factory fan-out, and auto-wraps OnChange/OnChangeAsync
// (AutoCallback) so invoking them re-renders the consumer.
//
// The framework's own component-style controls (samples MultiSelect/CheckboxGroup/RadioGroup) are
// the worked examples. In Render, collapse the typed validators for EditContext registration:
//   ctx?.RegisterFieldValidator(fid, (Delegate?)Validate ?? ValidateAsync, () => acc.Getter());
public interface IFormControl<T>
{
    // Bound mode — two-way binds an lvalue of type T and drives the ambient EditContext.
    Expression<Func<T>>? Bind { get; set; }
    Validate<T>? Validate { get; set; }
    ValidateAsync<T>? ValidateAsync { get; set; }
    Action<T>? AfterBind { get; set; }
    Func<T, Task>? AfterBindAsync { get; set; }

    // Controlled mode — the parent owns Value and is notified of changes.
    T? Value { get; set; }
    Callback<T>? OnChange { get; set; }
    CallbackAsync<T>? OnChangeAsync { get; set; }
}
