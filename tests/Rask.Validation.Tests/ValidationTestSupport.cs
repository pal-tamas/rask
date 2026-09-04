using Rask.Core.Forms;

namespace Rask.Validation.Tests;

/// <summary>
///     Shared helper for the built-in validation suite. Renders a real <c>Form</c> over
///     <paramref name="model" /> and hands its <c>EditContext</c> back for assertions.
/// </summary>
/// <remarks>
///     <para>
///         Note what is NOT in the markup below: no validator. The form registers the DataAnnotations
///         pass itself, so every test in this suite is also the regression test for that — if
///         auto-registration ever stops happening, all of them fail rather than one dedicated test.
///     </para>
///     <para>
///         This used to push an <c>EditContextScope</c> by hand. That scope is <c>internal</c> and only
///         <c>Form</c> ever pushes it, so hand-pushing simulated a form rather than exercising one — and it
///         was a path no consumer could reach. Going through <c>Form</c> is what an app author actually
///         does, so it covers the real registration path instead of an approximation of it.
///     </para>
/// </remarks>
[global::Rask.Core.RaskMarkup]
internal static partial class ValidationTestSupport
{
    /// <param name="model">The model to validate.</param>
    /// <param name="services">
    ///     Services the validator should see. A validator snapshots the render's provider when it
    ///     registers, so an attribute resolving a service through <c>ValidationContext.GetService</c> needs
    ///     it supplied here — the render is where it is read. Omit it to render with no services, which is
    ///     what an app without a configured provider looks like.
    /// </param>
    public static EditContext RegisterValidator<T>(T model, IServiceProvider? services = null)
        where T : class
    {
        EditContext? ctx = null;
        RaskTest.Render(() => Form.Model(model)[
            RaskTest.EditContextProbe(c => ctx = c)
        ], services);

        return ctx!;
    }

    /// <summary>
    ///     The same render with auto-validation turned off on the form itself, for the tests that assert
    ///     the opt-out actually opts out.
    /// </summary>
    /// <param name="model">The model that should NOT be validated.</param>
    /// <param name="services">Services for the render.</param>
    public static EditContext WithoutAutoValidation<T>(T model, IServiceProvider? services = null)
        where T : class
    {
        EditContext? ctx = null;
        RaskTest.Render(() => Form.Model(model).AutoValidate(false)[
            RaskTest.EditContextProbe(c => ctx = c)
        ], services);

        return ctx!;
    }
}
