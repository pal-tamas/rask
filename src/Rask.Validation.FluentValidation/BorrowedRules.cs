using System.Linq.Expressions;
using FluentValidation;
using FluentValidation.Internal;
using FluentValidation.Validators;

namespace Rask;

// A generated form model carries its aggregate's members one for one, but not the aggregate itself: there is no
// Supplier to hand Supplier's validator while the reader is still typing. So each rule the aggregate states for a member
// is re-declared on the model's member of the same name, its checks borrowed as they are — the very validator objects,
// asked about the model's value — and its failures said with the aggregate's own words (WithMessage, WithName).
//
// What cannot be borrowed is left to the save, which asks the aggregate itself: a rule under When/Unless (its condition
// reads an aggregate there is none of), a RuleForEach, and a member compared with another (GreaterThan(s => s.Start)).
internal static class BorrowedRules
{
    internal static void Borrow<TModel, TOwner, TValue>(
        InlineValidator<TModel> validator, IReadOnlyList<IValidator> owners, Expression<Func<TModel, TValue>> member, string name)
    {
        foreach (var (rule, checks) in Borrowable<TOwner, TValue>(owners, name))
        {
            var builder = validator.RuleFor(member);
            foreach (var check in checks)
            {
                AddCheck<TModel, TOwner, TValue, TValue>(builder, check, static value => value, static _ => true);
            }

            Named(builder, rule);
        }
    }

    internal static void BorrowLifted<TModel, TOwner, TValue>(
        InlineValidator<TModel> validator, IReadOnlyList<IValidator> owners, Expression<Func<TModel, TValue?>> member, string name)
        where TValue : struct
    {
        // A model's int? is the aggregate's int, or not given at all: what is not given, the model's own [Required] says.
        foreach (var (rule, checks) in Borrowable<TOwner, TValue>(owners, name))
        {
            var builder = validator.RuleFor(member);
            foreach (var check in checks)
            {
                AddCheck<TModel, TOwner, TValue, TValue?>(builder, check, static value => value.GetValueOrDefault(), static value => value.HasValue);
            }

            Named(builder, rule);
        }
    }

    // The aggregate's unconditional rules for the member, each with its unconditional checks.
    private static IEnumerable<(IValidationRule Rule, List<IRuleComponent> Checks)> Borrowable<TOwner, TValue>(
        IReadOnlyList<IValidator> owners, string name)
    {
        foreach (var owner in owners)
        {
            foreach (var rule in owner.CreateDescriptor().GetRulesForMember(name))
            {
                if (rule is not IValidationRule<TOwner, TValue> || rule.HasCondition || rule.HasAsyncCondition)
                {
                    continue;
                }

                var checks = rule.Components
                    .Where(static c => c is { HasCondition: false, HasAsyncCondition: false } && !ComparesMembers(c.Validator))
                    .ToList();
                if (checks.Count > 0)
                {
                    yield return (rule, checks);
                }
            }
        }
    }

    private static bool ComparesMembers(IPropertyValidator check) => check is IComparisonValidator { MemberToCompare: not null };

    private static void AddCheck<TModel, TOwner, TValue, TModelValue>(
        IRuleBuilder<TModel, TModelValue> builder, IRuleComponent check, Func<TModelValue, TValue> held, Func<TModelValue, bool> given)
    {
        switch (check.Validator)
        {
            case IAsyncPropertyValidator<TOwner, TValue> awaited:
                builder.SetAsyncValidator(new BorrowedAsyncCheck<TModel, TOwner, TValue, TModelValue>(awaited, check, held, given));
                break;
            case IPropertyValidator<TOwner, TValue> sync:
                builder.SetValidator(new BorrowedCheck<TModel, TOwner, TValue, TModelValue>(sync, check, held, given));
                break;
        }
    }

    // The aggregate's WithName, where it said one; otherwise the model's member is named as the aggregate's would be,
    // by its [Display] (FluentDisplayNames), which the generated model carries over.
    private static void Named<TModel, TValue>(IRuleBuilder<TModel, TValue> builder, IValidationRule rule)
    {
        if (builder is IRuleBuilderOptions<TModel, TValue> options && DisplayNameOf(rule) is { } name)
        {
            options.WithName(_ => DisplayNameOf(rule) ?? name);
        }
    }

    private static string? DisplayNameOf(IValidationRule rule)
    {
#pragma warning disable S1696, CA1031 // a WithName(s => …) reads the aggregate, and a model has none: such a name is the save's to say
        try
        {
            return rule.GetDisplayName(null!);
        }
        catch (NullReferenceException)
        {
            return null;
        }
#pragma warning restore S1696, CA1031
    }
}
