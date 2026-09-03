using FluentValidation;
using FluentValidation.Internal;
using FluentValidation.Results;
using Rask.Core.Forms;

namespace Rask.Validation.FluentValidation;

// The FluentValidation adapter. This used to be reachable only as the private Inner of a
// FluentValidationValidator component the author placed inside the form; the component is gone, the
// adapter is not, and every routing decision below is carried over unchanged.
//
// A form finds this through RaskValidators, which the generator fills in from the AbstractValidator<T>
// types in the compilation — so the author writes the validator and nothing else.
/// <summary>
///     Validates a form's model with a FluentValidation <see cref="IValidator" />, asynchronously — so
///     <c>MustAsync</c> rules work exactly like synchronous ones.
///     <para>
///         A <c>Form</c> finds the validator for its model on its own. Construct this yourself only when
///         you are driving an <see cref="EditContext" /> directly.
///     </para>
///     <para>
///         Client-side validation is a convenience, never a control: always validate again on the server.
///     </para>
/// </summary>
public sealed class FluentValidationFieldValidator : IAsyncFieldValidator
{
    private readonly IValidator _validator;

    /// <summary>
    ///     Wraps a FluentValidation validator as a Rask field validator.
    /// </summary>
    /// <param name="validator">The validator to run.</param>
    public FluentValidationFieldValidator(IValidator validator)
    {
        ArgumentNullException.ThrowIfNull(validator);
        _validator = validator;
    }

    /// <summary>
    ///     Runs every rule and attaches each failure to the field it names.
    /// </summary>
    /// <param name="context">The context being validated.</param>
    /// <param name="cancellationToken">Cancels the in-flight run.</param>
    public async ValueTask ValidateAsync(EditContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var validationContext = new ValidationContext<object>(context.Model);
        var result = await _validator.ValidateAsync(validationContext, cancellationToken).ConfigureAwait(false);

        foreach (var error in result.Errors)
        {
            RouteError(context, error);
        }
    }

    /// <summary>
    ///     Runs the rules that apply to one field.
    /// </summary>
    /// <param name="context">The context being validated.</param>
    /// <param name="field">The field to validate.</param>
    /// <param name="cancellationToken">Cancels the in-flight run.</param>
    public async ValueTask ValidateFieldAsync(
        EditContext context, FieldIdentifier field, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

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
