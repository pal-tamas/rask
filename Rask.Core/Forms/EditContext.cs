namespace Rask.Core.Forms;

public sealed class EditContext
{
    private readonly Dictionary<FieldIdentifier, FieldState> _states = new();
    private readonly List<IFieldValidator> _validators = new();

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
        ClearMessages(field);
        foreach (var v in _validators)
        {
            v.ValidateField(this, field);
        }

        ValidationStateChanged?.Invoke();
        return GetValidationMessages(field).Count == 0;
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
    }
}
