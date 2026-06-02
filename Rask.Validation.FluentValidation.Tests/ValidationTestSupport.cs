using FluentValidation;
using Rask.Core.Forms;

namespace Rask.Validation.FluentValidation.Tests;

/// <summary>
///     Shared validator-registration helper for the FluentValidation test suite. Pushes an
///     <see cref="EditContext" /> scope, renders a <c>FluentValidationValidator</c> bound to the
///     supplied <paramref name="validator" />, and returns the context for assertions. Imported
///     as a static using so call sites read <c>RegisterValidator(model, validator)</c>.
/// </summary>
internal static class ValidationTestSupport
{
    public static EditContext RegisterValidator<T>(T model, IValidator validator) where T : class
    {
        var ctx = new EditContext(model);
        using (EditContextScope.Push(ctx))
        {
            FluentValidationValidator(validator).ToHtml();
        }

        return ctx;
    }
}
