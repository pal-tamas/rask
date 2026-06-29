using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

/// <summary>Browser APIs section — <see cref="MediaSessionDemo" /> (<c>IMediaSession</c>).</summary>
[Route("browser/media-session")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class MediaSessionPage : Component
{
    protected override RenderResult Head => Title()["Media session — Browser APIs — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Media session",
            "Publish now-playing metadata to the OS (lock screen, media hub) and handle hardware media "
            + "keys via IMediaSession, so in-page audio/video feels like a native player. Metadata and "
            + "playback state are one-shot setters; action handlers are pushed from JS via a static "
            + "[JSInvokable], so one wiring serves both Server and WASM."),
        CodeSample(
            ["MediaSessionDemo.cs"],
            Notes: "SetMetadataAsync / SetPlaybackStateAsync are one-shot; SetActionHandlerAsync returns a "
                + "disposable subscription whose handler fires when the OS raises that media control.",
            Result: MediaSessionDemo())
    ];
}
