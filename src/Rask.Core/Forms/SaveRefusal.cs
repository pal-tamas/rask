using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Rask.Core.Diagnostics;
using Rask.Wire;

namespace Rask.Core.Forms;

// A DataAnnotations ValidationException thrown by a save is the save saying no, in words written for the
// person filling in the form: its message goes under the fields it names, else where a save's fault would.
internal static class SaveRefusal
{
    /// <summary>Shows what <paramref name="thrown" /> refused the save for, and says whether it was a refusal.</summary>
    internal static bool Show(Exception thrown, EditContext form, string formName)
    {
        if (Find(thrown) is not { } refusal)
        {
            return false;
        }

        var result = refusal.ValidationResult;
        var failure = new FieldFailure(
            result?.ErrorMessage ?? refusal.Message, [.. result?.MemberNames ?? []], Source: nameof(ValidationException));
        if (!FieldFailurePlacement.Place(form, [failure], null))
        {
            SaveFault.Say(form, failure.Message);
        }

        RaskDiagnostics.Report(
            RaskLogLevel.Information, "Rask.Forms", $"A form submit on {formName} was refused: {failure.Source}");
        return true;
    }

    // Exactly that type: a subclass is the app's own exception, and what it means is the app's to say.
    private static ValidationException? Find(Exception thrown) => thrown switch
    {
        ValidationException refusal when refusal.GetType() == typeof(ValidationException) => refusal,
        AggregateException many => many.Flatten().InnerExceptions.Select(Find).FirstOrDefault(found => found is not null),
        TargetInvocationException { InnerException: { } inner } => Find(inner),
        _ => null,
    };
}
