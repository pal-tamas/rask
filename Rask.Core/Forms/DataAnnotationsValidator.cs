using System.ComponentModel.DataAnnotations;

namespace Rask.Core.Forms;

public sealed class DataAnnotationsValidator : IFieldValidator
{
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
