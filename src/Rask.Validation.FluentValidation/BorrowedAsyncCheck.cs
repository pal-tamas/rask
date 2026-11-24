using FluentValidation;
using FluentValidation.Internal;
using FluentValidation.Validators;

namespace Rask;

// The same for an awaited check: MustAsync.
internal sealed class BorrowedAsyncCheck<TModel, TOwner, TValue, TModelValue>(
    IAsyncPropertyValidator<TOwner, TValue> check,
    IRuleComponent component,
    Func<TModelValue, TValue> held,
    Func<TModelValue, bool> given) : IAsyncPropertyValidator<TModel, TModelValue>
{
    public string Name => check.Name;

    public string GetDefaultMessageTemplate(string errorCode) => component.GetUnformattedErrorMessage();

    public async Task<bool> IsValidAsync(ValidationContext<TModel> context, TModelValue value, CancellationToken cancellation)
    {
        if (!given(value))
        {
            return true;
        }

        var borrowed = new ValidationContext<TOwner>(default!);
        if (await BorrowedContext.AsksAsync(() => check.IsValidAsync(borrowed, held(value), cancellation)).ConfigureAwait(false))
        {
            return true;
        }

        BorrowedContext.CarryOver(borrowed, context);
        return false;
    }
}
