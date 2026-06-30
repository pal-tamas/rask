using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;

namespace Rask.Wasm.Browser;

/// <summary>
///     Narrows the device chooser. Every field is optional; a set field must match. Null fields are omitted
///     (a serialized <c>null</c> would match nothing).
/// </summary>
/// <param name="VendorId">USB vendor id.</param>
/// <param name="ProductId">USB product id.</param>
/// <param name="UsagePage">Top-level collection usage page (e.g. <c>0x01</c> generic desktop).</param>
/// <param name="Usage">Top-level collection usage (e.g. <c>0x05</c> game pad).</param>
public sealed record HidDeviceFilter(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? VendorId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? ProductId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? UsagePage = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Usage = null);

/// <summary>Descriptor info for a HID device.</summary>
/// <param name="VendorId">USB vendor id.</param>
/// <param name="ProductId">USB product id.</param>
/// <param name="ProductName">Product string, if the device exposes one.</param>
public sealed record HidDeviceInfo(int VendorId, int ProductId, string? ProductName);

/// <summary>One inbound HID input report.</summary>
/// <param name="ReportId">The report id (0 when the device's reports are unnumbered).</param>
/// <param name="Data">The report payload bytes (excluding the report id).</param>
public sealed record HidInputReport(int ReportId, byte[] Data);

/// <summary>
///     Typed access to the WebHID API
///     (<see href="https://developer.mozilla.org/en-US/docs/Web/API/WebHID_API" />) — talk to a
///     human-interface device that isn't covered by a higher-level API: gamepads with custom reports,
///     keyboards with extra keys, simulation controls, point-of-sale hardware. Open a device, send output /
///     feature reports, and subscribe to its input-report stream. <b>WASM-only:</b>
///     <c>navigator.hid.requestDevice</c> needs <em>transient</em> user activation (a live gesture) and the
///     live device handle, which the Server/WebSocket round-trip can't carry, so it's registered only by the
///     WASM host. Chromium-family only at the time of writing, and a secure context (HTTPS / localhost) is
///     required.
/// </summary>
/// <remarks>
///     <para>
///         Call <see cref="RequestDevicesAsync" /> from a user-gesture handler: it shows the browser's chooser
///         and returns the granted devices (possibly several). The live <c>HIDDevice</c> is opaque to C#, so
///         the framework holds it JS-side under a minted id. <see cref="GetDevicesAsync" /> returns devices the
///         user already granted, without a prompt. <b>Dispose</b> a handle (or call
///         <see cref="IHidDevice.CloseAsync" />) to release it. Gate on <see cref="IsSupportedAsync" /> and
///         wrap calls in try/catch.
///     </para>
///     <para>
///         Input reports are <b>pushed</b> to the callback you pass <see cref="IHidDevice.WatchInputReportsAsync" />
///         (via a static <c>[JSInvokable]</c>), along with an optional disconnect signal — those callbacks may
///         call <c>StateHasChanged()</c> to re-render (subscription callbacks, so RASK026 doesn't apply).
///         Report payloads ride the boundary base64-encoded (raw <c>byte[]</c> args don't marshal across the
///         JS bridge).
///     </para>
/// </remarks>
public interface IHid
{
    /// <summary>Whether the browser supports WebHID (<c>"hid" in navigator</c>).</summary>
    ValueTask<bool> IsSupportedAsync();

    /// <summary>
    ///     Shows the device chooser (optionally narrowed by <paramref name="filters" /> — <c>null</c>/empty
    ///     means all devices) and returns the granted devices (zero if the user dismisses it). Must be called
    ///     from a user-gesture handler.
    /// </summary>
    ValueTask<IReadOnlyList<IHidDevice>> RequestDevicesAsync(HidDeviceFilter[]? filters = null);

    /// <summary>Returns the devices the user has already granted access to (no prompt).</summary>
    ValueTask<IReadOnlyList<IHidDevice>> GetDevicesAsync();
}

/// <summary>A handle to one HID device. Dispose (or <see cref="CloseAsync" />) to release it.</summary>
public interface IHidDevice : IAsyncDisposable
{
    /// <summary>What the device reports about itself.</summary>
    HidDeviceInfo Info { get; }

    /// <summary>Opens the device for I/O (required before sending reports or receiving input reports).</summary>
    ValueTask OpenAsync();

    /// <summary>Closes the device, releasing it for other applications.</summary>
    ValueTask CloseAsync();

    /// <summary>Sends an output report (<paramref name="reportId" /> 0 when the device's reports are unnumbered).</summary>
    ValueTask SendReportAsync(int reportId, byte[] data);

    /// <summary>Sends a feature report.</summary>
    ValueTask SendFeatureReportAsync(int reportId, byte[] data);

    /// <summary>Reads a feature report, returning its payload bytes.</summary>
    ValueTask<byte[]> ReceiveFeatureReportAsync(int reportId);

    /// <summary>
    ///     Starts delivering this device's input reports to <paramref name="onReport" />;
    ///     <paramref name="onDisconnect" /> (optional) fires once if the device is unplugged. Dispose the
    ///     returned handle to stop. The device must be open.
    /// </summary>
    ValueTask<IAsyncDisposable> WatchInputReportsAsync(
        Func<HidInputReport, Task> onReport, Func<Task>? onDisconnect = null);
}

/// <summary>
///     Infrastructure for <see cref="IHid" /> — routes a pushed input report (and the device-disconnect
///     signal) back to the right C# callbacks by device id. <b>Not for application use;</b> invoked only by
///     the framework's <c>__raskHid</c> JS helper via <c>window.DotNet.invokeMethodAsync</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class HidInterop
{
    private static int _nextToken;

    // Keyed by a per-watch token (not device id) so several watches on the same physical device — which the
    // browser hands back under one shared id — each get their own callbacks; input/disconnect fan out to
    // every watcher of the device id.
    private static readonly ConcurrentDictionary<int, Watcher> Watchers = new();

    internal static int Register(int deviceId, Func<HidInputReport, Task> onReport, Func<Task>? onDisconnect)
    {
        var token = Interlocked.Increment(ref _nextToken);
        Watchers[token] = new Watcher(deviceId, onReport, onDisconnect);
        return token;
    }

    internal static void Unregister(int token) => Watchers.TryRemove(token, out _);

    /// <summary>Infrastructure. Invoked by the JS bridge when an input report arrives; do not call.</summary>
    [JSInvokable("RaskHidInputReport")]
    public static async Task Input(int deviceId, int reportId, string base64)
    {
        HidInputReport? report = null;
        foreach (var w in Watchers.Values)
        {
            if (w.DeviceId != deviceId)
            {
                continue;
            }

            report ??= new HidInputReport(reportId, Convert.FromBase64String(base64));
            await w.OnReport(report);
        }
    }

    /// <summary>Infrastructure. Invoked by the JS bridge when a watched device is unplugged; do not call.</summary>
    [JSInvokable("RaskHidDisconnected")]
    public static async Task Disconnected(int deviceId)
    {
        foreach (var entry in Watchers)
        {
            // Remove on unplug (the device is gone) so the callback — and the component it captures — is released.
            if (entry.Value.DeviceId == deviceId && Watchers.TryRemove(entry.Key, out var w) && w.OnDisconnect is not null)
            {
                await w.OnDisconnect();
            }
        }
    }

    private readonly record struct Watcher(int DeviceId, Func<HidInputReport, Task> OnReport, Func<Task>? OnDisconnect);
}

/// <summary>Wire shape returned by the JS helper: the minted id plus the device descriptor info.</summary>
internal sealed record HidDeviceHandshake(int Id, HidDeviceInfo Info);

/// <summary>
///     Default <see cref="IHid" />, backed by the unified <see cref="IJSRuntime" />. The live
///     <c>HIDDevice</c> is opaque to C#, so the framework's <c>__raskHid</c> helper holds it under a minted id
///     and pushes input reports / disconnect back into <see cref="HidInterop" /> (static <c>[JSInvokable]</c>s
///     in this assembly). Report payloads cross base64-encoded.
/// </summary>
public sealed class Hid : IHid
{
    private readonly IJSRuntime _js;

    // Root HidInterop's [JSInvokable]s for the WASM trimmer — they're reached only via the JS
    // DotNetDispatcher (reflection), so without this they could be trimmed away.
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(HidInterop))]
    public Hid(IJSRuntime js) => _js = js;

    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => _js.InvokeAsync<bool>("__raskHid.isSupported");

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<IHidDevice>> RequestDevicesAsync(HidDeviceFilter[]? filters = null)
    {
        // (object) so the filter array crosses as a single argument, not spread by the params-style overload.
        var list = await _js.InvokeAsync<HidDeviceHandshake[]>("__raskHid.requestDevices", (object)(filters ?? []));
        return Wrap(list);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<IHidDevice>> GetDevicesAsync()
    {
        var list = await _js.InvokeAsync<HidDeviceHandshake[]>("__raskHid.getDevices");
        return Wrap(list);
    }

    private IReadOnlyList<IHidDevice> Wrap(HidDeviceHandshake[]? list) =>
        list is null ? [] : Array.ConvertAll(list, h => (IHidDevice)new Device(_js, h.Id, h.Info));

    private sealed class Device(IJSRuntime js, int id, HidDeviceInfo info) : IHidDevice
    {
        private readonly HashSet<int> _tokens = [];
        private bool _closed;

        public HidDeviceInfo Info => info;

        public ValueTask OpenAsync()
        {
            Guard();
            return js.InvokeVoidAsync("__raskHid.open", id);
        }

        public ValueTask SendReportAsync(int reportId, byte[] data)
        {
            ArgumentNullException.ThrowIfNull(data);
            Guard();
            return js.InvokeVoidAsync("__raskHid.sendReport", id, reportId, Convert.ToBase64String(data));
        }

        public ValueTask SendFeatureReportAsync(int reportId, byte[] data)
        {
            ArgumentNullException.ThrowIfNull(data);
            Guard();
            return js.InvokeVoidAsync("__raskHid.sendFeatureReport", id, reportId, Convert.ToBase64String(data));
        }

        public async ValueTask<byte[]> ReceiveFeatureReportAsync(int reportId)
        {
            Guard();
            var base64 = await js.InvokeAsync<string>("__raskHid.receiveFeatureReport", id, reportId);
            return Convert.FromBase64String(base64);
        }

        public async ValueTask<IAsyncDisposable> WatchInputReportsAsync(
            Func<HidInputReport, Task> onReport, Func<Task>? onDisconnect = null)
        {
            ArgumentNullException.ThrowIfNull(onReport);
            Guard();

            var token = HidInterop.Register(id, onReport, onDisconnect);
            lock (_tokens)
            {
                _tokens.Add(token);
            }

            try
            {
                await js.InvokeVoidAsync("__raskHid.watch", id);
            }
            catch
            {
                HidInterop.Unregister(token);
                lock (_tokens)
                {
                    _tokens.Remove(token);
                }

                throw;
            }

            return new Watch(this, token);
        }

        public async ValueTask CloseAsync()
        {
            if (_closed)
            {
                return;
            }

            _closed = true;

            int[] tokens;
            lock (_tokens)
            {
                tokens = [.. _tokens];
                _tokens.Clear();
            }

            foreach (var token in tokens)
            {
                HidInterop.Unregister(token);
                await js.InvokeVoidAsync("__raskHid.unwatch", id);
            }

            await js.InvokeVoidAsync("__raskHid.close", id);
        }

        public ValueTask DisposeAsync() => CloseAsync();

        private async ValueTask RemoveWatchAsync(int token)
        {
            bool removed;
            lock (_tokens)
            {
                removed = _tokens.Remove(token);
            }

            if (!removed)
            {
                return; // already torn down by CloseAsync
            }

            HidInterop.Unregister(token);
            await js.InvokeVoidAsync("__raskHid.unwatch", id);
        }

        private void Guard() => ObjectDisposedException.ThrowIf(_closed, typeof(IHidDevice));

        private sealed class Watch(Device owner, int token) : IAsyncDisposable
        {
            private bool _disposed;

            public async ValueTask DisposeAsync()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                await owner.RemoveWatchAsync(token);
            }
        }
    }
}
