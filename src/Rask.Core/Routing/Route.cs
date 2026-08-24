using System.Diagnostics.CodeAnalysis;

namespace Rask.Core.Routing;

public sealed record Route(
    [property: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors |
                                          DynamicallyAccessedMemberTypes.PublicProperties)]
    [param: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors |
                                       DynamicallyAccessedMemberTypes.PublicProperties)]
    Type PageType,
    string Template,
    IReadOnlyList<Route>? SubRoutes = null)
{
    /// <summary>
    ///     A route to <typeparamref name="T" /> at <paramref name="template" />, with any nested routes.
    /// </summary>
    /// <remarks>
    ///     Static members on the record rather than free functions somewhere importable: a route table is not
    ///     markup, so it is not reached through a chain, and a helper only findable through a
    ///     <c>using static</c> is a helper you have to know about before you can look it up. <c>Route.To</c>
    ///     is on the type the call already names.
    /// </remarks>
    /// <typeparam name="T">The page this route renders.</typeparam>
    /// <param name="template">The URL template, e.g. <c>"/products/{id}"</c>.</param>
    /// <param name="subRoutes">Routes nested under this one, rendered into its <c>Outlet</c>.</param>
    public static Route To<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors |
                                    DynamicallyAccessedMemberTypes.PublicProperties)]
    T>(string template, IReadOnlyList<Route>? subRoutes = null)
        where T : Component
        => new(typeof(T), template, subRoutes);

    /// <summary>
    ///     A route to <typeparamref name="T" /> at the template its own <c>[Route]</c> attribute declares.
    /// </summary>
    /// <typeparam name="T">The page this route renders.</typeparam>
    /// <param name="subRoutes">Routes nested under this one, rendered into its <c>Outlet</c>.</param>
    public static Route To<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors |
                                    DynamicallyAccessedMemberTypes.PublicProperties)]
    T>(IReadOnlyList<Route>? subRoutes = null)
        where T : Component
        => new(typeof(T), RouteTemplateResolver.GetLocalTemplate(typeof(T)), subRoutes);
}
