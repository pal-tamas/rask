using Microsoft.JSInterop;
using Rask.Core;
using Rask.Core.Browser;

namespace Rask.Wasm.Browser;

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
