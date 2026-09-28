using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     Default <see cref="IPerformance" />, backed by the unified <see cref="IJSRuntime" />. Both calls go
///     through the framework's <c>__raskPerf</c> helper — <c>now</c> for a stable <c>this</c> binding, and
///     <c>navigation</c> to pluck the navigation entry's milestones into a plain object.
/// </summary>
public sealed class Performance(IJSRuntime js) : IPerformance
{
    /// <inheritdoc />
    public ValueTask<double> NowAsync() => js.InvokeAsync<double>("__raskPerf.now");

    /// <inheritdoc />
    public ValueTask<NavigationTiming?> GetNavigationTimingAsync() =>
        js.InvokeAsync<NavigationTiming?>("__raskPerf.navigation");
}
