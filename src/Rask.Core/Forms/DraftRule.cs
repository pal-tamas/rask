using Rask.Values;

namespace Rask.Core.Forms;

// The rule of a field bound to a member of a value object's draft: what the value object's rules say of that member,
// then the rule the field already had. The form is told of the draft, so what its rules say of the whole is said too.
internal static class DraftRule
{
    internal static Delegate? Of<T>(Delegate? rule, ExpressionAccessor.Accessor accessor, EditContext context)
    {
        if (accessor.Target is not ValueDraft { Binding: { } binding })
        {
            return rule;
        }

        binding.Attach(context);
        var member = accessor.PropertyName;
        var said = new Validate<T>(_ => binding.Said(member));
        return rule is null ? said : RuleSequence<T>.Of(said, rule);
    }
}
