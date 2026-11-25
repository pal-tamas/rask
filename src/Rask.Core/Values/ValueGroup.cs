using System.Reflection;
using Rask.Core.Forms;

namespace Rask.Values;

// One value object of several values, as a form over a model of its own holds it: a draft of its members to type into,
// the shape its field is told (a range's), and how a Change<T> of it is read and made.
internal abstract class ValueGroup(Type value, IReadOnlyDictionary<string, PropertyInfo> members, Func<FieldShape>? shape)
{
    // The value object.
    public Type Value { get; } = value;

    // Its members, by name, as the draft holds them.
    public IReadOnlyDictionary<string, PropertyInfo> Members { get; } = members;

    // What a field bound to the whole of it is told; null for one declared from Value.
    public FieldShape? Shape => shape?.Invoke();

    // A new draft, every member empty.
    public abstract ValueDraft NewDraft();

    // Whether a property of this type is a Change<T> of the value object.
    public abstract bool IsChange(Type type);

    // The value object a change shows: what it sets or keeps; none for a clear.
    public abstract object? Shown(object change);

    public abstract ChangeKind KindOf(object change);

    public abstract object Set(object made);

    public abstract object Clear();

    public abstract object Kept(object? value);
}

// The same, typed: a value object is a class, so Change<Opening?> and Change<Opening> are the one Change<Opening>.
internal sealed class ValueGroup<TValue>(
    Func<ValueDraft> draft, IReadOnlyDictionary<string, PropertyInfo> members, Func<FieldShape>? shape)
    : ValueGroup(typeof(TValue), members, shape)
    where TValue : Value
{
    public override ValueDraft NewDraft() => draft();

    public override bool IsChange(Type type) => type == typeof(Change<TValue>);

    public override object? Shown(object change) => ((Change<TValue>)change).Value;

    public override ChangeKind KindOf(object change) => ((Change<TValue>)change).Kind;

    public override object Set(object made) => Change<TValue>.Set((TValue)made);

    public override object Clear() => Change<TValue>.Clear;

    public override object Kept(object? value) => Change<TValue>.Kept((TValue?)value);
}
