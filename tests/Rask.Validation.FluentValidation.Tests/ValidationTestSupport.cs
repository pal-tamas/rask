using FluentValidation;
using Rask.Core.Forms;

namespace Rask.Validation.FluentValidation.Tests;

/// <summary>
///     Shared validator-registration helper for the FluentValidation test suite. Renders a real
///     <c>Form</c> over <paramref name="model" /> with a <c>FluentValidationValidator</c> bound to the
///     supplied <paramref name="validator" />, and returns the form's context for assertions. Imported
///     as a static using so call sites read <c>RegisterValidator(model, validator)</c>.
/// </summary>
/// <remarks>
///     This used to push an <c>EditContextScope</c> by hand. That scope is <c>internal</c> and only
///     <c>Form</c> ever pushes it, so hand-pushing simulated a form rather than exercising one — and it
///     was a path no consumer could reach. Going through <c>Form</c> is what an app author actually
///     does, so it covers the real registration path instead of an approximation of it.
/// </remarks>
internal static class ValidationTestSupport
{
    public static EditContext RegisterValidator<T>(T model, IValidator validator) where T : class
    {
        EditContext? ctx = null;
        RaskTest.Render(() => Form(model)[
            FluentValidationValidator(validator),
            RaskTest.EditContextProbe(c => ctx = c)
        ]);

        return ctx!;
    }
}
