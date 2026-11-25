using Rask.Values;

namespace Rask.Core.Forms;

// A control bound to a value object a model holds as it is (`public TankCapacity? Size { get; set; }`): what was typed
// is the value it holds, read as that type and made into the value object through its TryCreate. The property is set
// only to a value its rules accept. Text they refuse, or that is no value of the held type, leaves the model as it was
// and is said under the field (UnreadValue) until a value they take arrives, so no save goes through with it.
internal static class ValueBinding
{
    internal static bool TryBind(ExpressionAccessor.Accessor acc, EditContext? ctx, FieldIdentifier fid, string raw, ValueField value)
    {
        if (IsEmpty(raw, value.Held))
        {
            return Emptied(acc, ctx, fid);
        }

        if (!BindingHelpers.TryParseTyped(value.Held, raw, out var held))
        {
            if (UnreadValue.Unparsed(value.Held, raw) is { } sentence)
            {
                ctx?.NoteUnread(fid, sentence);
            }

            return false;
        }

        if (value.Make(held, out var problems) is not { } made)
        {
            ctx?.NoteUnread(fid, string.Join(" ", GeneratedValueRules.Messages(problems)));
            return false;
        }

        acc.Setter(made);
        ctx?.NotifyFieldChanged(fid);
        return true;
    }

    // What a property that may not be null says when it is: the framework's one sentence for a value left empty.
    internal static string Required => GeneratedValueRules.Required()[0];

    // Whether the property may hold no value object at all: `TankCapacity? Size`.
    internal static bool MayBeEmpty(ExpressionAccessor.Accessor acc) =>
        BindingHelpers.IsNullableProperty(acc.Property, null, acc.PropertyType);

    // The rule a field bound to a value object runs: required first, where the property may not be null and holds
    // none (a field never typed into, then submitted), then the rule it already had.
    internal static Delegate? Rule<T>(Delegate? rule, ExpressionAccessor.Accessor acc)
    {
        if (ValueFields.Of(acc.PropertyType) is null || MayBeEmpty(acc))
        {
            return rule;
        }

        return rule is null ? Given<T>.Check : RuleSequence<T>.Of(Given<T>.Check, rule);
    }

    // A value object of text is empty when only blanks were typed: its Trim would leave nothing.
    private static bool IsEmpty(string raw, Type held) =>
        held == typeof(string) ? string.IsNullOrWhiteSpace(raw) : raw.Length == 0;

    // Emptied, a property that may be null is; one that may not keeps what it held and says it is required.
    private static bool Emptied(ExpressionAccessor.Accessor acc, EditContext? ctx, FieldIdentifier fid)
    {
        if (!MayBeEmpty(acc))
        {
            ctx?.NoteUnread(fid, Required);
            return false;
        }

        acc.Setter(null);
        ctx?.NotifyFieldChanged(fid);
        return true;
    }

    // Built once per type: a value object that must be given, given.
    private static class Given<T>
    {
        public static readonly Validate<T> Check = static value => value is null ? [Required] : [];
    }
}
