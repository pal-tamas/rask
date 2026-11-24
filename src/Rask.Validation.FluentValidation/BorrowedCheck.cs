using FluentValidation;
using FluentValidation.Internal;
using FluentValidation.Validators;

namespace Rask;

// One of an aggregate's checks, asked about a form model's value (BorrowedRules). The check is handed a context with no
// aggregate in it — the model has none — and what it appends to that context's message (its {MaxLength}, its
// {ComparisonValue}) is carried over to the model's, so the failure reads as the aggregate's own would.
internal sealed class BorrowedCheck<TModel, TOwner, TValue, TModelValue>(
    IPropertyValidator<TOwner, TValue> check,
    IRuleComponent component,
    Func<TModelValue, TValue> held,
    Func<TModelValue, bool> given) : IPropertyValidator<TModel, TModelValue>
{
    public string Name => check.Name;

    public string GetDefaultMessageTemplate(string errorCode) => component.GetUnformattedErrorMessage();

    public bool IsValid(ValidationContext<TModel> context, TModelValue value)
    {
        if (!given(value))
        {
            return true;
        }

        var borrowed = new ValidationContext<TOwner>(default!);
        if (BorrowedContext.Asks(() => check.IsValid(borrowed, held(value))))
        {
            return true;
        }

        BorrowedContext.CarryOver(borrowed, context);
        return false;
    }
}
