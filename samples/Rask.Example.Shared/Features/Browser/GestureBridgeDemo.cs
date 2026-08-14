using Rask.Core;
using Rask.Core.Browser;
using Rask.Core.Components;

namespace Rask.Example.Shared.Features;

/// <summary>
///     The full <c>GestureTrigger</c> family (Rask.Core) — the headless gesture bridge. Each trigger hands
///     <b>your</b> element a <c>data-rask-gesture</c> attribute; the shared client runs the activation-gated
///     browser API <b>inside the click gesture</b>, so these work on <b>every</b> host — the Server included,
///     where the imperative <c>IFullscreen</c> / <c>IEyeDropper</c> / … services can't be injected (a round-trip
///     would lose the transient user activation). <c>FullscreenTrigger</c> and <c>EyeDropperTrigger</c> are
///     joined here by <c>ScreenOrientationTrigger</c>, <c>InstallTrigger</c>, <c>MediaCaptureTrigger</c>, and
///     <c>PictureInPictureTrigger</c> (the last two target a <c>&lt;video&gt;</c> via its <c>ElementRef</c>).
/// </summary>
public sealed partial class GestureBridgeDemo(IMediaStreams streams) : Component
{
    private readonly ElementRef _preview = ElementRef.New();
    private string? _color;
    private string? _install;
    private MediaStreamId? _camera;

    protected override Component? Render() =>
        BsCard.Class(Bs.Join(Shadow.Sm, Border.None))[
            BsCardBody[
                BsStack.Gap(2).Align(BsAlign.Center).WrapItems(true).Class(Margin.Bottom(3))[
                    // Headless: we render our own buttons; the triggers just supply the gesture attribute.
                    FullscreenTrigger
                        .Template(g =>
                        Button.Type("button").Class("btn btn-primary btn-sm").Id("fullscreen-btn").Data(g)[
                            "Enter fullscreen"]),
                    ScreenOrientationTrigger
                        .Orientation("landscape")
                        .Template(g =>
                            Button
                                .Type("button")
                                .Class("btn btn-outline-primary btn-sm")
                                .Id("orientation-btn")
                                .Data(g)["Lock landscape"]),
                    InstallTrigger
                        .Template(g =>
                            Button
                                .Type("button")
                                .Class("btn btn-outline-success btn-sm")
                                .Id("install-btn")
                                .Data(g)["Install app"])
                        .OnOutcome(outcome =>
                        {
                            // No StateHasChanged: the trigger is a Component rather than an Element, so its
                            // callback is auto-wrapped and this demo repaints when the handler returns.
                            _install = outcome;
                            return Task.CompletedTask;
                        }),
                    _install is null
                        ? Span.Class("small text-secondary")["not prompted"]
                        : Span.Class("small")["install: ", Code.Id("install-outcome")[_install]]
                ],
                BsStack.Gap(2).Align(BsAlign.Center).WrapItems(true).Class(Margin.Bottom(2))[
                    EyeDropperTrigger
                        .Template(g =>
                            Button
                                .Type("button")
                                .Class("btn btn-outline-secondary btn-sm")
                                .Id("eyedropper-btn")
                                .Data(g)["Pick a colour"])
                        .OnColor(hex =>
                        {
                            _color = hex;
                            return Task.CompletedTask;
                        }),
                    _color is null
                        ? Span.Class("small text-secondary")["no colour picked"]
                        : Span.Class("d-inline-flex align-items-center gap-2 small")[
                            Span
                                .Id("eyedropper-swatch")
                                .Style("display:inline-block;width:1.25rem;height:1.25rem;border-radius:.25rem;"
                                       + $"border:1px solid #ccc;background:{_color}"),
                            Code.Id("eyedropper-value")[_color]]
                ],
                // MediaCaptureTrigger fills this <video> from the camera; PictureInPictureTrigger then pops
                // that same element out — both resolve the element from its ElementRef.
                BsStack.Gap(2).Align(BsAlign.Center).WrapItems(true)[
                    // For and Template are the required steps, so they come first: until both are named
                    // the receiver is still a pending-required wrapper and has no optional setters on it.
                    MediaCaptureTrigger
                        .For(_preview)
                        .Template(g =>
                            Button
                                .Type("button")
                                .Class("btn btn-outline-secondary btn-sm")
                                .Id("camera-btn")
                                .Data(g)["Start camera"])
                        .Video(true)
                        .FacingMode("user")
                        // OnStream keeps the started stream reachable from C# — the only way a Server-hosted
                        // app can hold one, and what makes the stop button below possible at all. No
                        // StateHasChanged: the trigger is a Component, so its callback is auto-wrapped and
                        // this demo repaints when the handler returns (RASK026).
                        .OnStream(id =>
                        {
                            _camera = id;
                            return Task.CompletedTask;
                        }),
                    Button
                        .Type("button")
                        .Class("btn btn-outline-secondary btn-sm")
                        .Id("camera-stop-btn")
                        .Disabled(_camera is null)
                        .OnClickAsync(StopCameraAsync)["Stop camera"],
                    PictureInPictureTrigger
                        .For(_preview)
                        .Template(g =>
                            Button
                                .Type("button")
                                .Class("btn btn-outline-secondary btn-sm")
                                .Id("pip-btn")
                                .Data(g)["Pop out video"]),
                    Video
                        .Ref(_preview)
                        .Id("gesture-preview")
                        .Muted(true)
                        .Style("width:12rem;max-width:100%;border-radius:.25rem;background:#000")
                ],
                Div.Class("small text-secondary mt-3")[
                    "Every button runs inside its own click gesture, so they all work on the Server too. ",
                    "Camera + picture-in-picture need HTTPS and a real device; install needs an installable PWA ",
                    "(", Code["AddRaskPwa"], "); orientation lock only takes effect while fullscreen (pair it ",
                    "with the fullscreen button on a phone); the eyedropper needs a Chromium browser. ",
                    "Stopping the camera goes through ", Code["IMediaStreams"], " on the id the capture ",
                    "trigger handed back — releasing the device and its hardware indicator."]
            ]
        ];

    // Stopping is not optional: a live stream holds the camera (and its indicator) open until every track
    // is stopped, and nothing else in the page will do it.
    private async Task StopCameraAsync()
    {
        if (_camera is not { } id)
        {
            return;
        }

        await streams.StopAsync(id);
        _camera = null;
    }
}
