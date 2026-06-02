using Rask.Core.Forms;

namespace Rask.Validation.DataAnnotations.Tests;

/// <summary>
///     Shared validator-registration helper for the DataAnnotations test suite. Pushes an
///     <see cref="EditContext" /> scope, renders a <c>DataAnnotationsValidator</c> (which
///     self-registers its <c>IFieldValidator</c> into the context), and hands the context back
///     for assertions. Imported as a static using so call sites read <c>RegisterValidator(m)</c>.
/// </summary>
internal static class ValidationTestSupport
{
    public static EditContext RegisterValidator<T>(T model) where T : class
    {
        var ctx = new EditContext(model);
        using (EditContextScope.Push(ctx))
        {
            DataAnnotationsValidator().ToHtml();
        }

        return ctx;
    }
}
