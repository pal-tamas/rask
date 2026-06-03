using FluentValidation;
using FluentValidation.Internal;
using FluentValidation.Results;
using Rask.Core;
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

    protected override RenderResult Render()
    {
        EditContextScope.Current?.AddValidator(new Inner(Validator));
        return Core.Components.Generated.Fragment();
    }

    private sealed class Inner : IAsyncFieldValidator
    {
        private readonly IValidator _validator;

        public Inner(IValidator validator) => _validator = validator;

        public async ValueTask ValidateAsync(EditContext context, CancellationToken cancellationToken)
        {
            var validationContext = new ValidationContext<object>(context.Model);
            var result = await _validator.ValidateAsync(validationContext, cancellationToken).ConfigureAwait(false);

            foreach (var error in result.Errors)
            {
                RouteError(context, error);
            }
        }

        public async ValueTask ValidateFieldAsync(
            EditContext context, FieldIdentifier field, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(field.FieldName))
            {
                return;
            }

            // Fast path: root-model field. Scope FV to a single property via
            // MemberNameValidatorSelector — keeps per-keystroke validation cheap when only one
            // field on the root changes, and the existing semantics are preserved exactly.
            if (ReferenceEquals(field.Model, context.Model))
            {
                var selector = new MemberNameValidatorSelector(new[] { field.FieldName });
                var rootCtx = new ValidationContext<object>(
                    context.Model, new PropertyChain(), selector);

                var rootResult = await _validator.ValidateAsync(rootCtx, cancellationToken).ConfigureAwait(false);
                foreach (var error in rootResult.Errors)
                {
                    if (error.PropertyName == field.FieldName)
                    {
                        context.AddValidationMessage(field, error.ErrorMessage);
                    }
                }

                return;
            }

            // Nested field. FluentValidation organises rules around the root validator's type,
            // so we can't easily target a single rule under a SetValidator / RuleForEach chain
            // with a selector. Run the full validator and filter results whose resolved owner
            // + terminal property match `field`.
            var fullCtx = new ValidationContext<object>(context.Model);
            var fullResult = await _validator.ValidateAsync(fullCtx, cancellationToken).ConfigureAwait(false);

            foreach (var error in fullResult.Errors)
            {
                if (string.IsNullOrEmpty(error.PropertyName))
                {
                    continue;
                }

                var resolved = ModelGraphWalker.Resolve(context.Model, error.PropertyName);
                if (resolved is { } r
                    && ReferenceEquals(r.Owner, field.Model)
                    && r.Property == field.FieldName)
                {
                    context.AddValidationMessage(field, error.ErrorMessage);
                }
            }
        }

        // Routes a FluentValidation error to the correct EditContext slot. Walks the dotted
        // property path against the root model so SetValidator / RuleForEach errors land on
        // the actual sub-instance, not on (rootModel, "Address.Street"). Falls back to a
        // form-level message on the root when the path can't be resolved to a terminal
        // property (empty path, bare collection item, stale index, …).
        private static void RouteError(EditContext context, ValidationFailure error)
        {
            var name = error.PropertyName ?? string.Empty;
            if (name.Length == 0)
            {
                context.AddValidationMessage(
                    new FieldIdentifier(context.Model, string.Empty),
                    error.ErrorMessage);
                return;
            }

            var resolved = ModelGraphWalker.Resolve(context.Model, name);
            if (resolved is { } r)
            {
                context.AddValidationMessage(
                    new FieldIdentifier(r.Owner, r.Property),
                    error.ErrorMessage);
                return;
            }

            // Couldn't resolve — surface as a form-level error on the root so it isn't lost.
            // ValidationSummary still picks it up; the original path stays in the message.
            context.AddValidationMessage(
                new FieldIdentifier(context.Model, string.Empty),
                error.ErrorMessage);
        }
    }
}
