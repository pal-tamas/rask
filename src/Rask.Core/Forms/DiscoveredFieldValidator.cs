namespace Rask.Core.Forms;

// What a Form actually registers. Holding the model type rather than a built validator is what makes
// editing a RuleFor and hot-reloading take effect: the rules are re-read on each validation run, not
// frozen at the render that first mounted the form.
internal sealed class DiscoveredFieldValidator : IAsyncFieldValidator
{
    private readonly Type _modelType;
    private readonly IServiceProvider? _services;

    internal DiscoveredFieldValidator(Type modelType, IServiceProvider? services)
    {
        _modelType = modelType;
        _services = services;
    }

    public ValueTask Validate(EditContext context, CancellationToken cancellationToken) =>
        RaskValidation.Resolve(_modelType, _services) is { } validator
            ? validator.Validate(context, cancellationToken)
            : default;

    public ValueTask ValidateField(
        EditContext context, FieldIdentifier field, CancellationToken cancellationToken) =>
        RaskValidation.Resolve(_modelType, _services) is { } validator
            ? validator.ValidateField(context, field, cancellationToken)
            : default;
}
