using FluentValidation;
using FluentValidation.Results;

namespace Rask;

// A row of a form model's list, validated by whatever validates its type when it is asked — the rules its own model
// borrows from its entity (ModelProjection) — so a root's model does not have to know them when it is registered.
internal sealed class ChildRowsValidator<TRow>(IServiceProvider? services) : IValidator<TRow>
{
    public ValidationResult Validate(TRow instance) => Validate(new ValidationContext<TRow>(instance));

    public Task<ValidationResult> ValidateAsync(TRow instance, CancellationToken cancellation = default) =>
        ValidateAsync(new ValidationContext<TRow>(instance), cancellation);

    public ValidationResult Validate(IValidationContext context) =>
        Row(context) is { } validator ? validator.Validate(context) : new ValidationResult();

    public Task<ValidationResult> ValidateAsync(IValidationContext context, CancellationToken cancellation = default) =>
        Row(context) is { } validator ? validator.ValidateAsync(context, cancellation) : Task.FromResult(new ValidationResult());

    public IValidatorDescriptor CreateDescriptor() => new ValidatorDescriptor<TRow>([]);

    public bool CanValidateInstancesOfType(Type type) => typeof(TRow).IsAssignableFrom(type);

    private IValidator? Row(IValidationContext context) =>
        context.InstanceToValidate is null ? null : RaskValidators.Find(typeof(TRow))?.Invoke(services) as IValidator;
}
