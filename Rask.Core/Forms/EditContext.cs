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

    public void AddValidationMessage(FieldIdentifier field, string message)
    {
        GetOrCreate(field).Messages.Add(message);
        ValidationStateChanged?.Invoke();
    }

    public bool Validate()
    {
        if (_asyncValidators.Count > 0 || HasAsyncDelegateValidators)
        {
            throw new InvalidOperationException("Async validators are registered; call ValidateAsync.");
        }

        ClearAllMessages();
        foreach (var v in _validators)
        {
            v.Validate(this);
        }

        InvokeSyncFieldDelegates();
        InvokeSyncFormDelegate();

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
        foreach (var v in _validators)
        {
            v.ValidateField(this, field);
        }

        InvokeSyncFieldDelegate(field);

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
        foreach (var v in _validators)
        {
            v.Validate(this);
        }

        if (_asyncValidators.Count > 0)
        {
            foreach (var v in _asyncValidators)
            {
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

                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        // Inline per-field delegates: sync invoked, async awaited. Each one isolates its own
        // exception so one bad delegate doesn't kill the whole submit pipeline.
        foreach (var pair in _fieldDelegates)
        {
            await InvokeFieldDelegateAsync(pair.Key, pair.Value, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }

        // Form-level delegate runs last so it sees settled per-field state and can apply
        // cross-field rules (e.g. "passwords match") with awareness of upstream errors.
        if (_formDelegate is not null)
        {
            await InvokeFormDelegateAsync(_formDelegate, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
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

        foreach (var v in _validators)
        {
            v.ValidateField(this, field);
        }

        var hasFieldDelegate = _fieldDelegates.TryGetValue(field, out var fieldReg);
        var fieldDelegateIsAsync = hasFieldDelegate && DelegateValidator.IsAsync(fieldReg.Validate);

        // Sync inline delegate runs synchronously alongside sync validators.
        if (hasFieldDelegate && !fieldDelegateIsAsync)
        {
            InvokeSyncFieldDelegate(field, fieldReg);
        }

        if (_asyncValidators.Count == 0 && !fieldDelegateIsAsync)
        {
            ValidationStateChanged?.Invoke();
            // Dispose the CTS we just installed; sync path doesn't need it.
            if (ReferenceEquals(state.Cts, cts))
            {
                state.Cts = null;
            }
            cts.Dispose();
            return state.Messages.Count == 0;
        }

        var wasZero = state.PendingCount == 0;
        state.PendingCount++;
        if (wasZero)
        {
            ValidationStateChanged?.Invoke();
        }

        try
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
            }

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
