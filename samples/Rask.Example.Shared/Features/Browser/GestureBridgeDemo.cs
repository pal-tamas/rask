using Rask.Core;
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
public sealed class GestureBridgeDemo : Component
{
    private readonly ElementRef _preview = ElementRef.New();
    private string? _color;
    private string? _install;

    protected override Component? Render() =>
        BsCard(Class: Bs.Join(Shadow.Sm, Border.None))[
            BsCardBody()[
                Div(Class: "d-flex gap-2 flex-wrap align-items-center mb-3")[
                    // Headless: we render our own buttons; the triggers just supply the gesture attribute.
                    FullscreenTrigger(g =>
                        Button(Type: "button", Class: "btn btn-primary btn-sm", Id: "fullscreen-btn", Data: g)[
                            "Enter fullscreen"]),
                    ScreenOrientationTrigger(Orientation: "landscape",
                        Template: g =>
                            Button(Type: "button", Class: "btn btn-outline-primary btn-sm", Id: "orientation-btn",
                                Data: g)["Lock landscape"]),
                    InstallTrigger(
                        OnOutcome: outcome =>
                        {
                            _install = outcome;
                            StateHasChanged(); // sanctioned pattern for an externally-pushed result
                            return Task.CompletedTask;
                        },
                        Template: g =>
                            Button(Type: "button", Class: "btn btn-outline-success btn-sm", Id: "install-btn",
                                Data: g)["Install app"]),
                    _install is null
                        ? Span(Class: "small text-secondary")["not prompted"]
                        : Span(Class: "small")["install: ", Code(Id: "install-outcome")[_install]]
                ],
                Div(Class: "d-flex gap-2 flex-wrap align-items-center mb-2")[
                    EyeDropperTrigger(
                        OnColor: hex =>
                        {
                            _color = hex;
                            StateHasChanged();
                            return Task.CompletedTask;
                        },
                        Template: g =>
                            Button(Type: "button", Class: "btn btn-outline-secondary btn-sm", Id: "eyedropper-btn",
                                Data: g)["Pick a colour"]),
                    _color is null
                        ? Span(Class: "small text-secondary")["no colour picked"]
                        : Span(Class: "d-inline-flex align-items-center gap-2 small")[
                            Span(Id: "eyedropper-swatch",
                                Style: "display:inline-block;width:1.25rem;height:1.25rem;border-radius:.25rem;"
                                       + $"border:1px solid #ccc;background:{_color}"),
                            Code(Id: "eyedropper-value")[_color]]
                ],
                // MediaCaptureTrigger fills this <video> from the camera; PictureInPictureTrigger then pops
                // that same element out — both resolve the element from its ElementRef.
                Div(Class: "d-flex gap-2 flex-wrap align-items-center")[
                    MediaCaptureTrigger(For: _preview, Video: true, FacingMode: "user",
                        Template: g =>
                            Button(Type: "button", Class: "btn btn-outline-secondary btn-sm", Id: "camera-btn",
                                Data: g)["Start camera"]),
                    PictureInPictureTrigger(For: _preview,
                        Template: g =>
                            Button(Type: "button", Class: "btn btn-outline-secondary btn-sm", Id: "pip-btn",
                                Data: g)["Pop out video"]),
                    Video(Ref: _preview, Id: "gesture-preview", Muted: true,
                        Style: "width:12rem;max-width:100%;border-radius:.25rem;background:#000")
                ],
                Div(Class: "small text-secondary mt-3")[
                    "Every button runs inside its own click gesture, so they all work on the Server too. ",
                    "Camera + picture-in-picture need HTTPS and a real device; install needs an installable PWA ",
                    "(", Code()["AddRaskPwa"], "); orientation lock only takes effect while fullscreen (pair it ",
                    "with the fullscreen button on a phone); the eyedropper needs a Chromium browser."]
            ]
        ];
}
