using Rask.Core.Routing;

namespace Rask.Example.Shared.Tests.Infrastructure;

// Navigator wrapper for tests. The real Navigator throws InvalidOperationException
// when called outside an event handler (EnsureInHandler). RunHandler wraps the test
// action in a Navigator.EnterHandler() scope so .Navigate / .SetQuery / etc. don't
// throw — equivalent to the framework's dispatcher entering a handler scope before
// invoking a click callback.
internal static class TestNavigator
{
    public static Navigator Create(RouteState? routeState = null, IDownloadSink? downloadSink = null)
        => new(routeState ?? new RouteState(), downloadSink);

    public static void RunHandler(Navigator nav, Action handler)
    {
        using var _ = nav.EnterHandler();
        handler();
    }

    public static T RunHandler<T>(Navigator nav, Func<T> handler)
    {
        using var _ = nav.EnterHandler();
        return handler();
    }
}
