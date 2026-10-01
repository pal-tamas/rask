using Rask.Core.Routing;

namespace Rask.Testing;

// The navigator a page under test dispatches its handlers through, over the test's own RouteState and
// IDownloadSink when it brought them: Go.To moves that RouteState, Download.File stages into that sink.
// A provider that already has one (Page.Visit's) is used as it is.
internal sealed class Navigating(IServiceProvider inner, RouteState route, Navigator navigator) : IServiceProvider
{
    public static IServiceProvider Over(IServiceProvider services)
    {
        if (services.GetService(typeof(Navigator)) is Navigator)
        {
            return services;
        }

        var route = services.GetService(typeof(RouteState)) as RouteState ?? new RouteState();
        return new Navigating(services, route, new Navigator(route, services.GetService(typeof(IDownloadSink)) as IDownloadSink));
    }

    public object? GetService(Type serviceType) => serviceType switch
    {
        _ when serviceType == typeof(Navigator) => navigator,
        _ when serviceType == typeof(RouteState) => route,
        _ => inner.GetService(serviceType),
    };
}
