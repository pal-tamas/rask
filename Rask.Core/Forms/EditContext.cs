using System.Diagnostics.CodeAnalysis;

namespace Rask.Core.Forms;

public sealed class EditContext
{
    private readonly Dictionary<FieldIdentifier, FieldState> _states = new();
    private readonly List<IFieldValidator> _validators = new();
    private readonly List<IAsyncFieldValidator> _asyncValidators = new();
    private readonly Dictionary<FieldIdentifier, DelegateRegistration> _fieldDelegates = new();
    private Delegate? _formDelegate;

    public EditContext(object model) => Model = model ?? throw new ArgumentNullException(nameof(model));

    public object Model { get; }

    internal IEnumerable<FieldIdentifier> RegisteredFields => _states.Keys;

    public event Action<FieldIdentifier>? FieldChanged;
    public event Action? ValidationStateChanged;

    public void AddValidator(IFieldValidator validator)
    {
        if (validator is null)
        {
            throw new ArgumentNullException(nameof(validator));
        }

        var t = validator.GetType();
        if (_validators.Any(v => v.GetType() == t))
        {
            return;
        }

        _validators.Add(validator);
    }

    public void AddValidator(IAsyncFieldValidator validator)
    {
        if (validator is null)
        {
            throw new ArgumentNullException(nameof(validator));
        }

        var t = validator.GetType();
        if (_asyncValidators.Any(v => v.GetType() == t))
        {
            return;
        }

        _asyncValidators.Add(validator);
    }

    // Per-field inline Validate delegate from Input/Select/Textarea factories. Passing
    // `validate: null` clears any prior registration so a re-render that drops the parameter
    // doesn't leave a stale callback in place. The value getter lets the dispatcher read the
    // current field value without reflecting on every validate call.
    public void RegisterFieldValidator(FieldIdentifier field, Delegate? validate, Func<object?> valueGetter)
    {
        if (validate is null)
        {
            _fieldDelegates.Remove(field);
            return;
        }

        _fieldDelegates[field] = new DelegateRegistration(validate, valueGetter);
    }

    // Convenience overload for callers that don't have a getter handy (tests, direct API
    // use). Uses reflection over the model's runtime type to resolve the value.
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "GetProperty on the model's runtime type — same constraint as DataAnnotations: " +
                        "the user-owned model's public properties are preserved by their binding setup.")]
    public void RegisterFieldValidator(FieldIdentifier field, Delegate? validate) =>
        RegisterFieldValidator(field, validate, () =>
            field.Model.GetType().GetProperty(field.FieldName)?.GetValue(field.Model));

    // Form-level inline Validate delegate. Null clears.
    public void RegisterFormValidator(Delegate? validate) => _formDelegate = validate;

    public bool HasAsyncValidators => _asyncValidators.Count > 0 || HasAsyncDelegateValidators;

    public bool HasAsyncDelegateValidators
    {
        get
        {
            if (_formDelegate is not null && DelegateValidator.IsAsync(_formDelegate))
            {
                return true;
            }

            foreach (var reg in _fieldDelegates.Values)
            {
                if (DelegateValidator.IsAsync(reg.Validate))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public bool IsValidating(FieldIdentifier field) =>
        _states.TryGetValue(field, out var s) && s.PendingCount > 0;

    public bool IsValidatingAny
    {
        get
        {
            foreach (var s in _states.Values)
            {
                if (s.PendingCount > 0) return true;
            }
            return false;
        }
    }

    public bool IsModified(FieldIdentifier field) =>
        _states.TryGetValue(field, out var s) && s.Modified;

    public bool IsTouched(FieldIdentifier field) =>
        _states.TryGetValue(field, out var s) && s.Touched;

    public IReadOnlyList<string> GetValidationMessages(FieldIdentifier field) =>
        _states.TryGetValue(field, out var s) ? s.Messages : Array.Empty<string>();

    public IEnumerable<string> GetValidationMessages()
    {
        foreach (var s in _states.Values)
        foreach (var m in s.Messages)
        {
            yield return m;
        }
    }

    public IReadOnlyList<ValidationEntry> GetValidationEntries()
    {
        var entries = new List<ValidationEntry>();
        foreach (var pair in _states)
        foreach (var m in pair.Value.Messages)
        {
            entries.Add(new ValidationEntry(pair.Key.FieldName, m));
        }

        return entries;
    }

    public bool HasValidationMessages() =>
        _states.Values.Any(s => s.Messages.Count > 0);

    public void NotifyFieldChanged(FieldIdentifier field)
    {
        var s = GetOrCreate(field);
        s.Modified = true;
        FieldChanged?.Invoke(field);
    }

    public void NotifyFieldTouched(FieldIdentifier field) =>
        GetOrCreate(field).Touched = true;

    public void ClearMessages(FieldIdentifier field)
    {
        if (_states.TryGetValue(field, out var s) && s.Messages.Count > 0)
        {
            s.Messages.Clear();
            ValidationStateChanged?.Invoke();
        }
    }

    public void ClearAllMessages()
    {
        var any = false;
        foreach (var s in _states.Values)
        {
            if (s.Messages.Count > 0)
            {
                s.Messages.Clear();
                any = true;
            }
        }

        if (any)
        {
            ValidationStateChanged?.Invoke();
        }
    }

    // Idempotent on (field, message): a second identical add is a no-op — neither the
    // list nor the event observe it. Matches ASP.NET Core ModelStateDictionary's
    // duplicate-error suppression and closes the door on the "same message rendered twice"
    // class of bugs that can otherwise arise when a validator runs against the same field
    // through more than one path (full Validate + per-field re-validate, re-entrant Render
    // on a sync-context resume, etc.).
    public void AddValidationMessage(FieldIdentifier field, string message)
    {
        var state = GetOrCreate(field);
        if (state.Messages.Contains(message, StringComparer.Ordinal))
        {
            return;
        }

        state.Messages.Add(message);
        ValidationStateChanged?.Invoke();
    }

    public bool Validate()
    {
        if (_asyncValidators.Count > 0 || HasAsyncDelegateValidators)
        {
            throw new InvalidOperationException("Async validators are registered; call ValidateAsync.");
        }

        ClearAllMessages();

        // Inline per-field delegates run first, then the form-level inline delegate,
        // then attribute-driven validators (DataAnnotations, FluentValidation, …) in
        // registration order. First-error-wins gates each later stage so a field stays
        // tied to the first rule that flagged it.
        InvokeSyncFieldDelegates();
        InvokeSyncFormDelegate();

        foreach (var v in _validators)
        {
            var pre = SnapshotMessageCounts();
            v.Validate(this);
            TrimGatedMessages(pre);
        }

        ValidationStateChanged?.Invoke();
        return !HasValidationMessages();
    }

    public bool ValidateField(FieldIdentifier field)
    {
        if (_asyncValidators.Count > 0 || HasAsyncDelegateValidators)
        {
            throw new InvalidOperationException("Async validators are registered; call ValidateFieldAsync.");
        }

        ClearMessages(field);

        // Inline field delegate first, then attribute-driven validators — short-circuit
        // as soon as any stage has produced a message for the field (first-error-wins).
        InvokeSyncFieldDelegate(field);

        if (GetValidationMessages(field).Count == 0)
        {
            foreach (var v in _validators)
            {
                v.ValidateField(this, field);
                if (GetValidationMessages(field).Count > 0)
                {
                    break;
                }
            }
        }

        ValidationStateChanged?.Invoke();
        return GetValidationMessages(field).Count == 0;
    }

    public async ValueTask<bool> ValidateAsync(CancellationToken cancellationToken = default)
    {
        // Supersede every in-flight per-field run before we re-validate from scratch.
        foreach (var s in _states.Values)
        {
            s.Cts?.Cancel();
        }

        ClearAllMessages();

        // Inline per-field delegates first: sync invoked, async awaited. Each one isolates
        // its own exception so one bad delegate doesn't kill the whole submit pipeline.
        foreach (var pair in _fieldDelegates)
        {
            await InvokeFieldDelegateAsync(pair.Key, pair.Value, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }

        // Form-level inline delegate runs next so cross-field rules (e.g. "passwords match")
        // can observe the per-field messages just produced above.
        if (_formDelegate is not null)
        {
            await InvokeFormDelegateAsync(_formDelegate, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }

        // Attribute-driven validators (DataAnnotations, FluentValidation, …) follow,
        // in registration order — sync first, then async. First-error-wins gates each
        // validator so a field that's already flagged stays tied to the earliest rule.
        foreach (var v in _validators)
        {
            var pre = SnapshotMessageCounts();
            v.Validate(this);
            TrimGatedMessages(pre);
        }

        if (_asyncValidators.Count > 0)
        {
            foreach (var v in _asyncValidators)
            {
                var pre = SnapshotMessageCounts();
                try
                {
                    await v.ValidateAsync(this, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    AddValidationMessage(new FieldIdentifier(Model, string.Empty), "Validation could not be completed.");
                }

                TrimGatedMessages(pre);
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        ValidationStateChanged?.Invoke();
        return !HasValidationMessages();
    }

    public async ValueTask<bool> ValidateFieldAsync(FieldIdentifier field, CancellationToken cancellationToken = default)
    {
        var state = GetOrCreate(field);

        // Latest-wins: cancel any prior in-flight run for this field.
        state.Cts?.Cancel();
        state.Cts?.Dispose();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        state.Cts = cts;

        ClearMessages(field);

        var hasFieldDelegate = _fieldDelegates.TryGetValue(field, out var fieldReg);
        var fieldDelegateIsAsync = hasFieldDelegate && DelegateValidator.IsAsync(fieldReg.Validate);

        // Fast sync path: nothing async to await. Inline first, then attribute-driven;
        // first-error-wins short-circuits as soon as any stage flags the field.
        if (!fieldDelegateIsAsync && _asyncValidators.Count == 0)
        {
            if (hasFieldDelegate)
            {
                InvokeSyncFieldDelegate(field, fieldReg);
            }

            if (state.Messages.Count == 0)
            {
                foreach (var v in _validators)
                {
                    v.ValidateField(this, field);
                    if (state.Messages.Count > 0)
                    {
                        break;
                    }
                }
            }

            ValidationStateChanged?.Invoke();
            if (ReferenceEquals(state.Cts, cts))
            {
                state.Cts = null;
            }
            cts.Dispose();
            return state.Messages.Count == 0;
        }

        // Async path. Enter the pending bookkeeping up front so the inline delegate (async
        // or sync) runs ahead of the attribute-driven validators — same order as the sync
        // path above.
        var wasZero = state.PendingCount == 0;
        state.PendingCount++;
        if (wasZero)
        {
            ValidationStateChanged?.Invoke();
        }

        try
        {
            if (hasFieldDelegate)
            {
                if (fieldDelegateIsAsync)
                {
                    try
                    {
                        var msgs = await DelegateValidator.InvokeAsync(
                            fieldReg.Validate, fieldReg.ValueGetter(), cts.Token).ConfigureAwait(false);
                        foreach (var m in msgs)
                        {
                            AddValidationMessage(field, m);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        return false;
                    }
                    catch (Exception)
                    {
                        AddValidationMessage(field, "Validation could not be completed.");
                    }

                    if (cts.IsCancellationRequested)
                    {
                        return false;
                    }
                }
                else
                {
                    InvokeSyncFieldDelegate(field, fieldReg);
                }
            }

            // First-error-wins: skip sync IFieldValidators once the field already has a
            // message from an earlier stage.
            if (state.Messages.Count == 0)
            {
                foreach (var v in _validators)
                {
                    v.ValidateField(this, field);
                    if (state.Messages.Count > 0)
                    {
                        break;
                    }
                }
            }

            if (state.Messages.Count == 0)
            {
                foreach (var v in _asyncValidators)
                {
                    try
                    {
                        await v.ValidateFieldAsync(this, field, cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return false;
                    }
                    catch (Exception)
                    {
                        AddValidationMessage(field, "Validation could not be completed.");
                    }

                    if (cts.IsCancellationRequested)
                    {
                        return false;
                    }

                    if (state.Messages.Count > 0)
                    {
                        break;
                    }
                }
            }

            return state.Messages.Count == 0;
        }
        finally
        {
            state.PendingCount--;
            if (state.PendingCount == 0)
            {
                if (ReferenceEquals(state.Cts, cts))
                {
                    state.Cts = null;
                }
                ValidationStateChanged?.Invoke();
            }

            cts.Dispose();
        }
    }

    public void TouchAllRegisteredFields()
    {
        foreach (var s in _states.Values)
        {
            s.Touched = true;
        }

        // Field delegates may target fields that haven't been touched by a binding yet;
        // make sure their messages survive the next per-keystroke gate.
        foreach (var field in _fieldDelegates.Keys)
        {
            GetOrCreate(field).Touched = true;
        }
    }

    // First-error-wins gating. We snapshot the per-field message count before invoking a
    // validator, then trim any messages that validator added on fields that already had
    // earlier messages. Two consequences:
    //   * Across stages — inline → form-level → sync IFieldValidator → async IAsyncFieldValidator
    //     — a field can only carry an error from the *first* stage that produced one.
    //   * Across validators within the sync or async stage, the earliest registered validator
    //     to flag a field wins; later validators don't pile on for the same field.
    // When the upstream rule passes on a re-validate, the snapshot is empty and the next
    // stage gets to run, which gives the "fix the error, the next rule kicks in" behaviour.
    private Dictionary<FieldIdentifier, int> SnapshotMessageCounts()
    {
        var snapshot = new Dictionary<FieldIdentifier, int>();
        foreach (var pair in _states)
        {
            if (pair.Value.Messages.Count > 0)
            {
                snapshot[pair.Key] = pair.Value.Messages.Count;
            }
        }

        return snapshot;
    }

    private void TrimGatedMessages(Dictionary<FieldIdentifier, int> preCounts)
    {
        foreach (var pair in preCounts)
        {
            if (_states.TryGetValue(pair.Key, out var s) && s.Messages.Count > pair.Value)
            {
                s.Messages.RemoveRange(pair.Value, s.Messages.Count - pair.Value);
            }
        }
    }

    internal FieldState GetOrCreate(FieldIdentifier field)
    {
        if (!_states.TryGetValue(field, out var s))
        {
            s = new FieldState();
            _states[field] = s;
        }

        return s;
    }

    private void InvokeSyncFieldDelegates()
    {
        foreach (var pair in _fieldDelegates)
        {
            InvokeSyncFieldDelegate(pair.Key, pair.Value);
        }
    }

    private void InvokeSyncFieldDelegate(FieldIdentifier field)
    {
        if (_fieldDelegates.TryGetValue(field, out var reg))
        {
            InvokeSyncFieldDelegate(field, reg);
        }
    }

    private void InvokeSyncFieldDelegate(FieldIdentifier field, DelegateRegistration reg)
    {
        try
        {
            foreach (var msg in DelegateValidator.InvokeSync(reg.Validate, reg.ValueGetter()))
            {
                AddValidationMessage(field, msg);
            }
        }
        catch
        {
            AddValidationMessage(field, "Validation could not be completed.");
        }
    }

    private void InvokeSyncFormDelegate()
    {
        if (_formDelegate is null)
        {
            return;
        }

        try
        {
            foreach (var msg in DelegateValidator.InvokeSync(_formDelegate, Model))
            {
                AddValidationMessage(new FieldIdentifier(Model, string.Empty), msg);
            }
        }
        catch
        {
            AddValidationMessage(new FieldIdentifier(Model, string.Empty), "Validation could not be completed.");
        }
    }

    private async ValueTask InvokeFieldDelegateAsync(
        FieldIdentifier field, DelegateRegistration reg, CancellationToken cancellationToken)
    {
        if (DelegateValidator.IsAsync(reg.Validate))
        {
            try
            {
                var msgs = await DelegateValidator.InvokeAsync(
                    reg.Validate, reg.ValueGetter(), cancellationToken).ConfigureAwait(false);
                foreach (var m in msgs)
                {
                    AddValidationMessage(field, m);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                AddValidationMessage(field, "Validation could not be completed.");
            }
        }
        else
        {
            InvokeSyncFieldDelegate(field, reg);
        }
    }

    private async ValueTask InvokeFormDelegateAsync(Delegate validate, CancellationToken cancellationToken)
    {
        var formField = new FieldIdentifier(Model, string.Empty);
        if (DelegateValidator.IsAsync(validate))
        {
            try
            {
                var msgs = await DelegateValidator.InvokeAsync(validate, Model, cancellationToken).ConfigureAwait(false);
                foreach (var m in msgs)
                {
                    AddValidationMessage(formField, m);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                AddValidationMessage(formField, "Validation could not be completed.");
            }
        }
        else
        {
            InvokeSyncFormDelegate();
        }
    }

    internal sealed class FieldState
    {
        public List<string> Messages = new();
        public bool Modified;
        public bool Touched;
        public int PendingCount;
        public CancellationTokenSource? Cts;
    }

    private readonly struct DelegateRegistration
    {
        public DelegateRegistration(Delegate validate, Func<object?> valueGetter)
        {
            Validate = validate;
            ValueGetter = valueGetter;
        }

        public Delegate Validate { get; }
        public Func<object?> ValueGetter { get; }
    }
}
