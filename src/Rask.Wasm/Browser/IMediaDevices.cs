using Microsoft.JSInterop;
using Rask.Core;
using Rask.Core.Browser;

namespace Rask.Wasm.Browser;

/// <summary>One media input/output device from <c>navigator.mediaDevices.enumerateDevices</c>.</summary>
/// <param name="DeviceId">Stable id to pin a specific device (empty until permission is granted).</param>
/// <param name="Kind"><c>"audioinput"</c>, <c>"videoinput"</c>, or <c>"audiooutput"</c>.</param>
/// <param name="Label">Human-readable name (empty until permission is granted).</param>
/// <param name="GroupId">Groups devices belonging to the same physical hardware.</param>
public sealed record MediaDeviceInfo(string DeviceId, string Kind, string Label, string GroupId);

/// <summary>What to capture in a <see cref="IMediaDevices.GetUserMediaAsync" /> request.</summary>
/// <param name="Video">Capture the camera.</param>
/// <param name="Audio">Capture the microphone.</param>
/// <param name="FacingMode">
///     Preferred camera when <paramref name="Video" /> is set — <c>"user"</c> (front) or
///     <c>"environment"</c> (rear). Ignored on devices with one camera.
/// </param>
public sealed record MediaConstraints(bool Video = true, bool Audio = false, string? FacingMode = null);

/// <summary>
///     Typed access to Media Capture / <c>getUserMedia</c>
///     (<see href="https://developer.mozilla.org/en-US/docs/Web/API/MediaDevices" />) — capture the camera,
///     microphone, or screen and show it in a <c>&lt;video&gt;</c>, for photo capture, video calls, QR
///     scanning, or screen recording. <b>WASM-only:</b> <c>getUserMedia</c> needs <em>transient</em> user
///     activation and the live document (and a secure context), which the Server/WebSocket round-trip can't
///     provide, so it's registered only by the WASM host.
/// </summary>
/// <remarks>
///     <para>
///         The live <c>MediaStream</c> can't cross interop, so the framework holds it JS-side under a minted
///         id and hands back an <see cref="IMediaStreamHandle" /> — attach it to a <c>&lt;video&gt;</c> via
///         <see cref="IMediaStreamHandle.AttachToAsync" />, and <b>dispose</b> it (or call
///         <see cref="IMediaStreamHandle.StopAsync" />) to stop every track and release the camera/mic (the
///         hardware indicator stays on until you do). Call from a user-gesture handler; a denial surfaces as
///         a <see cref="JSException" /> — gate on <see cref="IsSupportedAsync" /> and wrap in try/catch.
///     </para>
///     <para>
///         Device <c>Label</c>/<c>DeviceId</c> are empty in <see cref="EnumerateDevicesAsync" /> until the
///         user has granted capture permission at least once.
///     </para>
/// </remarks>
public interface IMediaDevices
{
    /// <summary>Whether the browser supports media capture (<c>navigator.mediaDevices.getUserMedia</c>).</summary>
    ValueTask<bool> IsSupportedAsync();

    /// <summary>Lists the available cameras, microphones, and speakers.</summary>
    ValueTask<IReadOnlyList<MediaDeviceInfo>> EnumerateDevicesAsync();

    /// <summary>
    ///     Requests a camera/microphone stream per <paramref name="constraints" /> and returns a handle to
    ///     it. Must be called from a user-gesture handler; throws on denial.
    /// </summary>
    ValueTask<IMediaStreamHandle> GetUserMediaAsync(MediaConstraints constraints);

    /// <summary>
    ///     Requests a screen-share stream (<c>getDisplayMedia</c>) and returns a handle to it. Must be called
    ///     from a user-gesture handler; throws on denial.
    /// </summary>
    ValueTask<IMediaStreamHandle> GetDisplayMediaAsync();
}

/// <summary>A handle to one live <c>MediaStream</c>. Dispose (or <see cref="StopAsync" />) to stop all tracks.</summary>
public interface IMediaStreamHandle : IAsyncDisposable
{
    /// <summary>
    ///     The stream's framework id — the same currency <see cref="IMediaStreams" />,
    ///     <c>MediaCaptureTrigger</c> and <see cref="IWebRtc" /> deal in. Pass it to
    ///     <c>IPeerConnection.AddStreamAsync</c> to send this stream to a peer.
    /// </summary>
    MediaStreamId Id { get; }

    /// <summary>Shows the stream in the <c>&lt;video&gt;</c> referenced by <paramref name="video" /> and plays it.</summary>
    ValueTask AttachToAsync(ElementRef video);

    /// <summary>Stops every track, releasing the camera/microphone (turns off the hardware indicator).</summary>
    ValueTask StopAsync();
}

/// <summary>
///     Default <see cref="IMediaDevices" />, backed by the unified <see cref="IJSRuntime" />. The live
///     <c>MediaStream</c> is opaque to C#, so the framework's <c>__raskMedia</c> helper holds each under a
///     minted id; the handle attaches the stream to a video element (handed across as an
///     <see cref="ElementRef" />) and stops its tracks by id.
/// </summary>
public sealed class MediaDevices(IJSRuntime js) : IMediaDevices
{
    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => js.InvokeAsync<bool>("__raskMedia.isSupported");

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<MediaDeviceInfo>> EnumerateDevicesAsync() =>
        await js.InvokeAsync<MediaDeviceInfo[]>("__raskMedia.enumerate");

    /// <inheritdoc />
    public async ValueTask<IMediaStreamHandle> GetUserMediaAsync(MediaConstraints constraints)
    {
        ArgumentNullException.ThrowIfNull(constraints);
        var id = await js.InvokeAsync<int>("__raskMedia.getUserMedia", constraints);
        return new StreamHandle(js, id);
    }

    /// <inheritdoc />
    public async ValueTask<IMediaStreamHandle> GetDisplayMediaAsync()
    {
        var id = await js.InvokeAsync<int>("__raskMedia.getDisplayMedia");
        return new StreamHandle(js, id);
    }

    private sealed class StreamHandle(IJSRuntime js, int id) : IMediaStreamHandle
    {
        private bool _stopped;

        public MediaStreamId Id => new(id);

        public ValueTask AttachToAsync(ElementRef video)
        {
            ArgumentNullException.ThrowIfNull(video);
            return js.InvokeVoidAsync("__raskMedia.attach", id, video);
        }

        public ValueTask StopAsync()
        {
            if (_stopped)
            {
                return ValueTask.CompletedTask;
            }

            _stopped = true;
            return js.InvokeVoidAsync("__raskMedia.stop", id);
        }

        public ValueTask DisposeAsync() => StopAsync();
    }
}
