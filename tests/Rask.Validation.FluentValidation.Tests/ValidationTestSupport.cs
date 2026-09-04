using FluentValidation;
using Rask.Core.Forms;

namespace Rask.Validation.FluentValidation.Tests;

/// <summary>
///     Shared helper for the FluentValidation suite. Points <c>RaskValidators</c> at the supplied
///     <paramref name="validator" /> for <c>T</c>, renders a real <c>Form</c> over
///     <paramref name="model" /> with nothing declared in the markup, and returns the form's context
///     for assertions. Imported as a static using so call sites read
///     <c>RegisterValidator(model, validator)</c>.
/// </summary>
/// <remarks>
///     <para>
///         These tests each supply their own rules for the same handful of model types, which
///         compile-time discovery cannot express — one model has one validator, by design (RASKVAL001).
///         So the helper uses the manual registration hook, which is also the hook an app reaches for
///         when a validator cannot be discovered. Discovery itself is covered separately by
///         <c>GeneratedRegistrationTests</c>, which declares a real <c>AbstractValidator&lt;T&gt;</c>
///         and registers nothing.
///     </para>
///     <para>
///         Note what is NOT in the markup: no validator component. The form finds the validator for its
///         model on its own.
///     </para>
/// </remarks>
[global::Rask.Core.RaskMarkup]
internal static partial class ValidationTestSupport
{
    /// <param name="model">The model to validate.</param>
    /// <param name="validator">The rules to run for it.</param>
    public static EditContext RegisterValidator<T>(T model, IValidator validator) where T : class
    {
        RaskValidators.Register(typeof(T), _ => validator);

        EditContext? ctx = null;
        RaskTest.Render(() => Form.Model(model)[
            RaskTest.EditContextProbe(c => ctx = c)
        ]);

        return ctx!;
    }
}
