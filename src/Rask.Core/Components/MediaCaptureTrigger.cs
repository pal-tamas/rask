using System.Globalization;
using System.Text.Json;
using Rask.Core.Browser;

namespace Rask.Core.Components;

/// <summary>
///     Start a camera/microphone stream from a click gesture and attach it to a <c>&lt;video&gt;</c> (works on
///     Server, unlike the imperative <c>IMediaDevices</c>). Needs a secure (HTTPS) context; the stream stays
///     attached to <see cref="For" /> (muted, autoplaying) until the page navigates away.
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
    ///     Invoked with the started stream's <see cref="MediaStreamId" />, so the stream stays reachable
    ///     from C# after the gesture — stop it with <see cref="IMediaStreams.StopAsync" />, re-attach it to
    ///     another <c>&lt;video&gt;</c>, or send it to a peer with <c>IPeerConnection.AddStreamAsync</c>.
    ///     Not invoked when the user refuses. This is the only way a <b>Server</b>-hosted app can hold on to
    ///     a captured stream.
    /// </summary>
    public Callback<MediaStreamId> OnStream { get; set; }

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
        var sink = OnResult.HasValue || OnStream.HasValue
            ? new Callback<string?>(Dispatch)
            : default;
        return Template!(GestureBridge.Attr("media.start", sink, arg: constraints, el: For.Id));
    }

    // The capability resolves the stream's id, or "denied". The bridge posts exactly one result per click,
    // so both callbacks are fed from that one value here rather than costing a second round trip — and
    // OnResult keeps the "granted"/"denied" vocabulary it always had.
    private async Task Dispatch(string? result)
    {
        // Invariant: the id is minted by JS and crosses the wire as a JS-formatted integer.
        var started = int.TryParse(
            result, NumberStyles.Integer, CultureInfo.InvariantCulture, out var streamId);

        if (started)
        {
            await OnStream.Invoke(new MediaStreamId(streamId)).ConfigureAwait(false);
        }

        await OnResult.Invoke(started ? "granted" : "denied").ConfigureAwait(false);
    }
}
