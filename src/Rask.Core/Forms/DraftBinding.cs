using System.Reflection;
using Rask.Values;

namespace Rask.Core.Forms;

// What binds a value object's draft to the property of the model that holds it — `Opening? Start`, or
// `Change<Opening?> Start` — as its members are typed into. The draft shows what the property holds until a member is
// typed into; from then on, each member typed in makes the value object again, and the property is set only to one its
// rules take whole: a property that may be absent is emptied when every member is, a change is a Set of a new value, a
// Clear once emptied, and stays a Keep while untouched or typed back to what it keeps. What the rules say is said under
// the member it is about (`Start.Quantity`), what they say of the whole under the property (`Start`).
internal sealed class DraftBinding
{
    private static readonly char[] MemberEnds = ['.', '['];

    private readonly ValueDraft _draft;
    private readonly object _owner;
    private readonly PropertyInfo _property;
    private readonly ValueGroup _group;
    private readonly bool _change;
    private readonly bool _mayBeEmpty;

    // The value object the draft last showed or wrote: a property found holding another was changed by code.
    private object? _seen;

    // What a change kept, the last time it was found keeping: typed back to it, it keeps again.
    private object? _kept;
    private bool _keeps;

    // Whether a member was typed into since the draft last showed the property.
    private bool _typed;
    private IReadOnlyList<Problem> _problems = [];
    private EditContext? _context;

    internal DraftBinding(ValueDraft draft, object owner, PropertyInfo property, ValueGroup group)
    {
        _draft = draft;
        _owner = owner;
        _property = property;
        _group = group;
        _change = group.IsChange(property.PropertyType);
        _mayBeEmpty = _change
            ? BindingHelpers.IsNullableTypeArgument(property)
            : BindingHelpers.IsNullableProperty(property, null, property.PropertyType);
        Show(Shown());
    }

    // The property itself, where what is said of the whole value object is kept.
    internal FieldIdentifier Whole => new(_owner, _property.Name);

    // Asked on every bind: the property set by code since shows its new value, and what was typed into the old is dropped.
    internal void Follow()
    {
        var shown = Shown();
        if (!Equals(shown, _seen))
        {
            Show(shown);
        }
    }

    // The form the draft's controls are on, told what the rules say of the whole under the property.
    internal void Attach(EditContext context)
    {
        if (ReferenceEquals(_context, context))
        {
            return;
        }

        _context = context;
        context.RegisterFieldValidator(Whole, new Validate<object?>(_ => Said(null)), () => _property.GetValue(_owner));
    }

    // What the rules say of one member — or of the whole, for null — as a field's rule reads it.
    internal IEnumerable<string> Said(string? member)
    {
        var problems = _typed ? _problems : Untyped();
        return problems.Count == 0 ? [] : [.. problems.Where(p => Belongs(p, member)).Select(static p => p.Message)];
    }

    // A member was typed into: the value object is made again, and written when its rules take it.
    internal void Typed()
    {
        _typed = true;
        var wrote = true;
        if (_draft.IsEmpty && (_mayBeEmpty || _change))
        {
            _problems = [];
            Write(_change ? Emptied() : null);
        }
        else if (_draft.Make(out var problems) is { } made)
        {
            _problems = [];
            Write(_change ? Changed(made) : made);
        }
        else
        {
            _problems = problems;
            wrote = false;
        }

        if (_context is not { } context)
        {
            return;
        }

        if (wrote)
        {
            context.NotifyFieldChanged(Whole);
        }

        // A sibling's answer may have changed with this member: what it already says is asked again.
        foreach (var member in _group.Members.Keys)
        {
            context.Recheck(new FieldIdentifier(_draft, member));
        }

        context.Recheck(Whole);
    }

    // Never typed into: nothing to say, unless the property may not be empty and is — a submit of a form whose group was
    // never filled. A value kept as it was stored is not asked again, whatever its rules say now.
    private IReadOnlyList<Problem> Untyped()
    {
        if (_mayBeEmpty || _change || _property.GetValue(_owner) is not null)
        {
            return [];
        }

        return _draft.Make(out var problems) is null ? problems : [Problem.Whole(ValueBinding.Required)];
    }

    // The value object the property shows: what it holds, or what its change sets or keeps.
    private object? Shown()
    {
        var held = _property.GetValue(_owner);
        if (!_change || held is null)
        {
            return held;
        }

        if (_group.KindOf(held) == ChangeKind.Keep)
        {
            (_kept, _keeps) = (_group.Shown(held), true);
        }

        return _group.Shown(held);
    }

    private void Show(object? shown)
    {
        _draft.Fill(shown);
        (_seen, _typed, _problems) = (shown, false, []);
    }

    // What was written is what the draft now shows, so the next bind does not fill it again over what was typed.
    private void Write(object? held)
    {
        _property.SetValue(_owner, held);
        _seen = _change && held is not null ? _group.Shown(held) : held;
    }

    // Every member emptied: a change of a value that may be absent clears it; one that may not keeps it.
    private object Emptied() =>
        !_mayBeEmpty || (_keeps && _kept is null) ? _group.Kept(_kept) : _group.Clear();

    // Typed back to what it keeps, a change keeps; anything else is set.
    private object Changed(object made) => _keeps && Equals(made, _kept) ? _group.Kept(_kept) : _group.Set(made);

    // A problem about a member names it first (`Quantity`, `Vehicle.Value`); one that names none is the whole value's.
    private bool Belongs(Problem problem, string? member)
    {
        var named = problem.Field is { Length: > 0 } field ? field.Split(MemberEnds)[0] : null;
        var ofMember = named is not null && _group.Members.ContainsKey(named);
        return member is null ? !ofMember : ofMember && string.Equals(named, member, StringComparison.Ordinal);
    }
}
