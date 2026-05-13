using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace Rask.Core.Forms;

public sealed class DataAnnotationsValidator : IFieldValidator
{
    // DataAnnotationsValidator reflects over the form model — when trimming, users must preserve
    // the model's properties (typically via `[DynamicallyAccessedMembers]` on the binding source,
    // or by referencing the model from a [Route]'d page, which roots it via DynamicDependency).
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "ValidationContext/Validator reflect over the model. The user-owned model type is " +
                        "preserved through binding annotations on their app, not Rask.Core itself.")]
    public void Validate(EditContext context)
    {
        var results = new List<ValidationResult>();
        var ctx = new ValidationContext(context.Model);
        Validator.TryValidateObject(context.Model, ctx, results, true);

        foreach (var r in results)
        {
            var members = r.MemberNames.ToList();
            if (members.Count == 0)
            {
                context.AddValidationMessage(
                    new FieldIdentifier(context.Model, string.Empty),
                    r.ErrorMessage ?? "Invalid value.");
                continue;
            }

            foreach (var m in members)
            {
                context.AddValidationMessage(
                    new FieldIdentifier(context.Model, m),
                    r.ErrorMessage ?? "Invalid value.");
            }
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Same as Validate(): user-owned model preservation is the caller's responsibility.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "GetProperty on the model's runtime type — preserved by the user's binding setup.")]
    public void ValidateField(EditContext context, FieldIdentifier field)
    {
        if (!ReferenceEquals(field.Model, context.Model))
        {
            return;
        }

        var prop = context.Model.GetType().GetProperty(field.FieldName);
        if (prop is null)
        {
            return;
        }

        var ctx = new ValidationContext(context.Model) { MemberName = field.FieldName };
        var results = new List<ValidationResult>();
        Validator.TryValidateProperty(prop.GetValue(context.Model), ctx, results);

        foreach (var r in results)
        {
            context.AddValidationMessage(field, r.ErrorMessage ?? "Invalid value.");
        }
    }
}
