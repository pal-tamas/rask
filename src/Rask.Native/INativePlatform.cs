using Microsoft.Extensions.DependencyInjection;

namespace Rask.Native;

/// <summary>
///     A native platform module (iOS / Android) that contributes native C# backends for the browser/device
///     API interfaces (e.g. <c>IGeolocation</c> → CoreLocation / Android <c>LocationManager</c>). A platform
///     head passes one to <see cref="NativeAppHost.UsePlatform" />; the host invokes <see cref="Register" />
///     in <see cref="NativeAppHost.RunLocalAsync{TApp}" /> <b>before</b> wiring the JS-backed fallbacks, so any
///     interface the platform backs natively wins (native-first) and the rest fall back to the WebView's JS.
/// </summary>
public interface INativePlatform
{
    /// <summary>
    ///     Registers this platform's native backends on <paramref name="services" />. Use TryAdd semantics —
    ///     <see cref="Rask.Core.Browser.RaskBrowserApis.AddBrowserApi{TService,TImpl}" /> or
    ///     <c>TryAddSingleton</c> — so an explicit app registration made before running still wins.
    /// </summary>
    void Register(IServiceCollection services);
}
