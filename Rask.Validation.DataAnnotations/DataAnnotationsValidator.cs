using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using Rask.Core;
using Rask.Core.Components;
using Rask.Core.Forms;

namespace Rask.Validation.DataAnnotations;

// Opt-in DataAnnotations validator. Place inside a Form as a child:
//
//   Form<RegistrationModel>(_model, OnValidSubmit: ...)[
//       DataAnnotationsValidator(),
//       Input(() => _model.Email),
//       ValidationMessage(() => _model.Email, ...)
//   ]
//
// On render it pulls EditContextScope.Current and registers an IFieldValidator that defers
// to System.ComponentModel.DataAnnotations.Validator. EditContext.AddValidator dedups by
// runtime type, so re-renders are idempotent. TagName is null — the component emits no DOM.
public sealed class DataAnnotationsValidator : Component
{
    // The validator reads mutable EditContext state via AddValidator, but its OWN output
    // (the empty Fragment) doesn't change between renders. Cache opt-out is unnecessary here
    // — re-rendering only re-registers the validator, which AddValidator no-ops on dedup.

    protected override Component Render()
    {
        EditContextScope.Current?.AddValidator(new Inner());
        return Rask.Core.Components.Components.Fragment();
    }

    private sealed class Inner : IFieldValidator
    {
        // DataAnnotationsValidator reflects over the form model — when trimming, users must
        // preserve the model's properties (typically via `[DynamicallyAccessedMembers]` on
        // the binding source, or by referencing the model from a [Route]'d page, which roots
        // it via DynamicDependency).
        [UnconditionalSuppressMessage("Trimming", "IL2026",
            Justification = "ValidationContext/Validator reflect over the model. The user-owned model type is " +
                            "preserved through binding annotations on their app, not Rask.Validation.DataAnnotations itself.")]
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
}
