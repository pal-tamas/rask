using Rask.Core.Forms;

namespace Rask.Values;

// One value object of one value, as a form holds it.
internal sealed class ValueField(Type held, Func<object, object?> read, ValueField.Maker make, Func<FieldShape>? shape)
{
    internal delegate object? Maker(object? held, out IReadOnlyList<Problem> problems);

    // The type of the value it holds: string, decimal, int, DateOnly…
    public Type Held { get; } = held;

    // The value a value object holds.
    public object? Read(object value) => read(value);

    // The value object made from what was typed, or null with what its rules said against it.
    public object? Make(object? value, out IReadOnlyList<Problem> problems) => make(value, out problems);

    // What a control bound to it is told; null for one declared from Value, which says nothing of its own.
    public FieldShape? Shape => shape?.Invoke();
}
