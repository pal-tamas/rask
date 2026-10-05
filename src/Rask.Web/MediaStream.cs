using Microsoft.JSInterop;

namespace Rask.Web;

#pragma warning disable CA1711 // MDN's name for it, as the generated half of this class has: renaming it would break every MDN example
public sealed partial class MediaStream
#pragma warning restore CA1711
{
    /// <summary>
    ///     MDN's <c>MediaStream</c> over a stream a Rask service handed you as a handle — the camera
    ///     <c>Trigger.MediaCapture</c>'s <c>OnStream</c> started, or a peer's from <c>RtcHandlers.OnTrack</c> — kept, as one
    ///     Rask.Web keeps is: disposing of either lets the browser drop it.
    /// </summary>
    /// <example>
    ///     <code>
    ///     await using var camera = MediaStream.From(stream);
    ///     foreach (var track in await camera.GetTracks()) await track.Stop();   // the camera light goes off
    ///     </code>
    /// </example>
    public static Types.MediaStream From(IJSObjectReference stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return new Types.MediaStream(JsChain.Adopt(stream));
    }
}
