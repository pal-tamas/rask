using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>The browser's effective connection quality (<c>NetworkInformation.effectiveType</c>).</summary>
public enum EffectiveConnectionType
{
    /// <summary>Unknown / not reported by the browser.</summary>
    Unknown,

    /// <summary><c>slow-2g</c>.</summary>
    Slow2g,

    /// <summary><c>2g</c>.</summary>
    TwoG,

    /// <summary><c>3g</c>.</summary>
    ThreeG,

    /// <summary><c>4g</c> (or better).</summary>
    FourG
}

/// <summary>A snapshot of the network connection (the Network Information API).</summary>
/// <param name="EffectiveType">Effective connection class, derived from recent throughput/RTT.</param>
/// <param name="Downlink">Estimated downlink bandwidth in megabits per second.</param>
/// <param name="Rtt">Estimated effective round-trip time in milliseconds.</param>
/// <param name="SaveData">Whether the user has requested reduced data usage (Data Saver).</param>
public sealed record NetworkStatus(EffectiveConnectionType EffectiveType, double Downlink, double Rtt, bool SaveData);

// Wire shape for __raskApi.network — effectiveType arrives as the browser's lowercase string and is
// mapped to the typed enum in C#. Registered for trim-safe source-gen in RaskBrowserJsonContext.
internal sealed record NetworkReading(string? EffectiveType, double Downlink, double Rtt, bool SaveData);

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

/// <summary>
///     Default <see cref="INetworkInfo" />, backed by the unified <see cref="IJSRuntime" />.
///     <c>navigator.connection</c> is a live, vendor-prefixed object, so the read goes through the
///     framework's <c>__raskApi.network</c> helper, which returns a plain
///     <c>{ effectiveType, downlink, rtt, saveData }</c> snapshot (mapped to <see cref="NetworkStatus" />).
/// </summary>
public sealed class NetworkInfo(IJSRuntime js) : INetworkInfo
{
    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => js.InvokeAsync<bool>("__raskApi.networkSupported");

    /// <inheritdoc />
    public async ValueTask<NetworkStatus?> GetStatusAsync()
    {
        var reading = await js.InvokeAsync<NetworkReading?>("__raskApi.network");
        return reading is null
            ? null
            : new NetworkStatus(MapType(reading.EffectiveType), reading.Downlink, reading.Rtt, reading.SaveData);
    }

    private static EffectiveConnectionType MapType(string? type) => type switch
    {
        "slow-2g" => EffectiveConnectionType.Slow2g,
        "2g" => EffectiveConnectionType.TwoG,
        "3g" => EffectiveConnectionType.ThreeG,
        "4g" => EffectiveConnectionType.FourG,
        _ => EffectiveConnectionType.Unknown
    };
}
