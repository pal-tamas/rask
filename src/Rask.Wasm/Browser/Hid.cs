using System.Diagnostics.CodeAnalysis;
using Microsoft.JSInterop;

namespace Rask.Wasm.Browser;

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
    /// <summary>
    ///     Creates the service. Registered for you — inject <see cref="IHid" /> rather than
    ///     constructing this.
    /// </summary>
    /// <param name="js">The JS interop runtime the wrapper calls through.</param>
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(HidInterop))]
    public Hid(IJSRuntime js) => _js = js;

    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => _js.InvokeAsync<bool>("__raskHid.isSupported");

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<IHidDevice>> RequestDevicesAsync(HidDeviceFilter[]? filters = null)
    {
        // (object) so the filter array crosses as a single argument, not spread by the params-style overload.
        var list = await _js.InvokeAsync<HidDeviceHandshake[]>("__raskHid.requestDevices", (object)(filters ?? [])).ConfigureAwait(false);
        return Wrap(list);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<IHidDevice>> GetDevicesAsync()
    {
        var list = await _js.InvokeAsync<HidDeviceHandshake[]>("__raskHid.getDevices").ConfigureAwait(false);
        return Wrap(list);
    }

    private IHidDevice[] Wrap(HidDeviceHandshake[]? list) =>
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
            var base64 = await js.InvokeAsync<string>("__raskHid.receiveFeatureReport", id, reportId).ConfigureAwait(false);
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
                await js.InvokeVoidAsync("__raskHid.watch", id).ConfigureAwait(false);
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
                await js.InvokeVoidAsync("__raskHid.unwatch", id).ConfigureAwait(false);
            }

            await js.InvokeVoidAsync("__raskHid.close", id).ConfigureAwait(false);
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
            await js.InvokeVoidAsync("__raskHid.unwatch", id).ConfigureAwait(false);
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
                await owner.RemoveWatchAsync(token).ConfigureAwait(false);
            }
        }
    }
}
