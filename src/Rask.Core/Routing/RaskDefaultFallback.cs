using System.Runtime.CompilerServices;
using Rask.Core.Components;

namespace Rask.Core.Routing;

internal static class RaskDefaultFallback
{
    // A library module initializer is the point: the fallback must be registered before any app code runs.
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Init() =>
        RouteRegistry.SetDefaultFallback(typeof(DefaultNotFoundPage));
}
