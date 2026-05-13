using System.Diagnostics.CodeAnalysis;

namespace Rask.Core.Routing;

public sealed record Route(
    [property: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors |
                                          DynamicallyAccessedMemberTypes.PublicProperties)]
    [param: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors |
                                       DynamicallyAccessedMemberTypes.PublicProperties)]
    Type PageType,
    string Template,
    IReadOnlyList<Route>? SubRoutes = null);
