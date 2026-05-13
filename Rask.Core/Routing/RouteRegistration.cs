using System.Diagnostics.CodeAnalysis;

namespace Rask.Core.Routing;

public readonly record struct RouteRegistration(
    [property: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors |
                                          DynamicallyAccessedMemberTypes.PublicProperties)]
    [param: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors |
                                       DynamicallyAccessedMemberTypes.PublicProperties)]
    Type PageType,
    string Template,
    Type? Parent);
