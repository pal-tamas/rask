namespace Rask.Core.Forms;

public sealed class EditContext
{
    private readonly Dictionary<FieldIdentifier, FieldState> _states = new();
    private readonly List<IFieldValidator> _validators = new();
    private readonly List<IAsyncFieldValidator> _asyncValidators = new();

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

    public bool HasAsyncValidators => _asyncValidators.Count > 0;

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
        if (_asyncValidators.Count > 0)
        {
            throw new InvalidOperationException("Async validators are registered; call ValidateAsync.");
        }

        ClearAllMessages();
        foreach (var v in _validators)
        {
            v.Validate(this);
        }

        ValidationStateChanged?.Invoke();
        return !HasValidationMessages();
    }

    public bool ValidateField(FieldIdentifier field)
    {
        if (_asyncValidators.Count > 0)
        {
            throw new InvalidOperationException("Async validators are registered; call ValidateFieldAsync.");
        }

        ClearMessages(field);
        foreach (var v in _validators)
        {
            v.ValidateField(this, field);
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

        if (_asyncValidators.Count == 0)
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

    internal sealed class FieldState
    {
        public List<string> Messages = new();
        public bool Modified;
        public bool Touched;
        public int PendingCount;
        public CancellationTokenSource? Cts;
    }
}
