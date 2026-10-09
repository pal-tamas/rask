using Rask.Wire;

namespace Rask.Site.Features;

public sealed class RouteNameTaken() : Exception("The route name is taken."), IFieldFailures
{
    public IReadOnlyList<FieldFailure> Failures { get; } =
        [new("A route with this name already exists.", [nameof(RouteModel.Name)])];
}
