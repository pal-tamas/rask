using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Rask.Core.Browser;
using Rask.Core.Live;

namespace Rask.Core.Components;

/// <summary>
///     Start a camera/microphone stream from a click gesture and attach it to a <c>&lt;video&gt;</c> — on Server too,
///     where a browser that only allows a capture in the click would refuse a round trip's. Needs a secure (HTTPS)
///     context; the stream stays attached to <see cref="For" /> (muted, autoplaying) until you stop its tracks
///     (<see cref="OnStream" />) or the page navigates away.
///     <see cref="OnResult" /> receives <c>"granted"</c> once the stream starts, or <c>"denied"</c> if the user
///     refuses.
/// </summary>
[RaskChainGroup(typeof(global::Rask.Trigger))]
public sealed class MediaCaptureTrigger : Component
{
    /// <summary>The <c>&lt;video&gt;</c> element the captured stream is attached to.</summary>
    public required ElementRef For { get; set; }

    /// <summary>Capture the microphone. Defaults to <c>false</c>.</summary>
#pragma warning disable CA1805 // the initializer is what keeps Audio an optional chain step (no initializer = required)
    public bool Audio { get; set; } = false;
#pragma warning restore CA1805

    /// <summary>Capture the camera. Defaults to <c>true</c>.</summary>
    public bool Video { get; set; } = true;

    /// <summary>Optional camera facing mode — <c>"user"</c> (front) or <c>"environment"</c> (rear).</summary>
    public string? FacingMode { get; set; }

    /// <summary>Invoked with <c>"granted"</c> when the stream starts, or <c>"denied"</c> if the user refuses.</summary>
    public Callback<string?> OnResult { get; set; }

    /// <summary>
    ///     Invoked with a handle to the started <c>MediaStream</c>, kept in the browser until you dispose of it, so
    ///     the stream stays reachable from C# after the gesture: Rask.Web's <c>MediaStream.From(stream)</c> is MDN's
    ///     <c>MediaStream</c> on it (<c>GetTracks()</c>, then <c>Stop()</c> each, to release the camera), and
    ///     <c>IPeerConnection.AddStreamAsync</c> sends it to a peer. Not invoked when the user refuses. This is the
    ///     only way a <b>Server</b>-hosted app can hold on to a captured stream.
    /// </summary>
    public Callback<IJSObjectReference> OnStream { get; set; }

    /// <summary>Renders your trigger element; its click starts the capture and attaches it to <see cref="For" />.</summary>
    public required Func<IReadOnlyDictionary<string, string?>, Component> Template { get; set; }

    /// <inheritdoc />
    protected override Component Render()
    {
        var constraints = JsonSerializer.Serialize(
            new GestureMediaConstraints(Video, Audio, FacingMode),
            RaskBrowserJsonContext.Default.GestureMediaConstraints);
        // Stay fire-and-forget when the app wants no result: passing a non-null sink would register a
        // callback id on every render for nobody to consume.
        var js = AmbientServices.Current?.GetService<IJSRuntime>();
        var sink = OnResult.HasValue || OnStream.HasValue
            ? new Callback<string?>(result => Dispatch(result, js))
            : default;
        return Template!(GestureBridge.Attr("media.start", sink, arg: constraints, el: For.Id));
    }

    // The capability resolves the id the stream is handed over under, or "denied". The bridge posts exactly
    // one result per click, so both callbacks are fed from that one value — and OnResult keeps the
    // "granted"/"denied" vocabulary it always had. The stream itself is taken by its id, as a handle, from the
    // page the trigger rendered in; taken even when only OnResult listens, so the browser forgets the id.
    private async Task Dispatch(string? result, IJSRuntime? js)
    {
        // Invariant: the id is minted by JS and crosses the wire as a JS-formatted integer.
        var started = int.TryParse(
            result, NumberStyles.Integer, CultureInfo.InvariantCulture, out var streamId);

        if (started && js is not null)
        {
            var stream = await js.InvokeAsync<IJSObjectReference>("__raskMedia.take", streamId).ConfigureAwait(false);
            if (OnStream.HasValue)
            {
                await OnStream.Invoke(stream).ConfigureAwait(false);
            }
            else
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }
        }

        await OnResult.Invoke(started ? "granted" : "denied").ConfigureAwait(false);
    }
}
