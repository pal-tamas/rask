using Rask.Core.Forms;

namespace Rask.Validation.DataAnnotations.Tests;

/// <summary>
///     Shared validator-registration helper for the DataAnnotations test suite. Renders a real
///     <c>Form</c> over <paramref name="model" /> with a <c>DataAnnotationsValidator</c> child (which
///     self-registers its <c>IFieldValidator</c> into the form's context) and hands that context back
///     for assertions. Imported as a static using so call sites read <c>RegisterValidator(m)</c>.
/// </summary>
/// <remarks>
///     This used to push an <c>EditContextScope</c> by hand. That scope is <c>internal</c> and only
///     <c>Form</c> ever pushes it, so hand-pushing simulated a form rather than exercising one — and it
///     was a path no consumer could reach. Going through <c>Form</c> is what an app author actually
///     does, so it covers the real registration path instead of an approximation of it.
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
            DataAnnotationsValidator,
            RaskTest.EditContextProbe(c => ctx = c)
        ], services);

        return ctx!;
    }
}
