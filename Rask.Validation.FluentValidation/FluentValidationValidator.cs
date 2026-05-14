using FluentValidation;
using FluentValidation.Internal;
using Rask.Core;
using Rask.Core.Components;
using Rask.Core.Forms;

namespace Rask.Validation.FluentValidation;

// Opt-in FluentValidation validator. Place inside a Form as a child:
//
//   Form<SignupModel>(_model, OnValidSubmit: ...)[
//       FluentValidationValidator(new SignupModelValidator()),
//       Input(() => _model.Username),
//       ValidationMessage(() => _model.Username, ...)
//   ]
//
// On render it pulls EditContextScope.Current and registers an IAsyncFieldValidator that
// forwards to FluentValidation's ValidateAsync. EditContext.AddValidator dedups by runtime
// type, but two FluentValidationValidator instances would carry different IValidator targets
// — so re-renders rely on the validator instance being kept stable by the framework's
// component caching, and a FRESH FluentValidationValidator component in a re-render with
// a different IValidator instance will be deduped to the first registration.
public sealed class FluentValidationValidator : Component
{
    public required IValidator Validator { get; set; }

    protected override Component Render()
    {
        EditContextScope.Current?.AddValidator(new Inner(Validator));
        return Rask.Core.Components.Components.Fragment();
    }

    private sealed class Inner : IAsyncFieldValidator
    {
        private readonly IValidator _validator;

        public Inner(IValidator validator) => _validator = validator;

        public async ValueTask ValidateAsync(EditContext context, CancellationToken cancellationToken)
        {
            var validationContext = new global::FluentValidation.ValidationContext<object>(context.Model);
            var result = await _validator.ValidateAsync(validationContext, cancellationToken).ConfigureAwait(false);

            foreach (var error in result.Errors)
            {
                var field = new FieldIdentifier(context.Model, error.PropertyName ?? string.Empty);
                context.AddValidationMessage(field, error.ErrorMessage);
            }
        }

        public async ValueTask ValidateFieldAsync(
            EditContext context, FieldIdentifier field, CancellationToken cancellationToken)
        {
            if (!ReferenceEquals(field.Model, context.Model) || string.IsNullOrEmpty(field.FieldName))
            {
                return;
            }

            // Scope FV to a single property — keeps per-keystroke validation cheap when only
            // one field changes. MemberNameValidatorSelector matches by property name; rules
            // without an explicit RuleFor target (model-level rules) are skipped here, which
            // matches the per-field semantics callers expect.
            var selector = new MemberNameValidatorSelector(new[] { field.FieldName });
            var validationContext = new global::FluentValidation.ValidationContext<object>(
                context.Model, new PropertyChain(), selector);

            var result = await _validator.ValidateAsync(validationContext, cancellationToken).ConfigureAwait(false);

            foreach (var error in result.Errors)
            {
                if (error.PropertyName == field.FieldName)
                {
                    context.AddValidationMessage(field, error.ErrorMessage);
                }
            }
        }
    }
}
