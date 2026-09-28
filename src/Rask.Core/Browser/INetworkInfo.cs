namespace Rask.Core.Browser;

/// <summary>
///     Typed access to the Network Information API
///     (<see href="https://developer.mozilla.org/en-US/docs/Web/API/Network_Information_API" />) — read the
///     current connection quality to adapt loading (defer heavy assets on <c>slow-2g</c>, honour Data
///     Saver). Pairs with <see cref="INavigatorInfo.OnLineAsync" /> (online/offline) for the fuller
///     picture. Works on <b>both transports</b>; inject it through a component constructor and read from an
///     event handler or lifecycle hook.
/// </summary>
/// <remarks>
///     Support is partial (Chromium-based browsers; not Firefox/Safari) — gate on
///     <see cref="IsSupportedAsync" />; <see cref="GetStatusAsync" /> returns <c>null</c> where the API is
///     unavailable.
/// </remarks>
public interface INetworkInfo
{
    /// <summary>Whether the browser exposes the Network Information API (<c>navigator.connection</c>).</summary>
    ValueTask<bool> IsSupportedAsync();

    /// <summary>
    ///     Reads the current <see cref="NetworkStatus" />, or <c>null</c> when the API isn't supported.
    /// </summary>
    ValueTask<NetworkStatus?> GetStatusAsync();
}
