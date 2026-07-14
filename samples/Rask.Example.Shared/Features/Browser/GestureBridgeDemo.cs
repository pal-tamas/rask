using Rask.Core;
using Rask.Core.Components;

namespace Rask.Example.Shared.Features;

/// <summary>
///     <c>GestureTrigger</c> family (Rask.Core) — the headless gesture bridge. <c>FullscreenTrigger</c> /
///     <c>EyeDropperTrigger</c> hand <b>your</b> element a <c>data-rask-gesture</c> attribute; the shared
///     client runs the activation-gated browser API <b>inside the click gesture</b>, so these work on
///     <b>every</b> host — the Server included, where the imperative <c>IFullscreen</c> / <c>IEyeDropper</c>
///     services can't be injected (a round-trip would lose the transient user activation). The eyedropper's
///     picked colour is pushed back to the <c>OnColor</c> callback.
/// </summary>
public sealed class GestureBridgeDemo : Component
{
    private string? _color;

    protected override Component? Render() =>
        BsCard(Class: Bs.Join(Shadow.Sm, Border.None))[
            BsCardBody()[
                Div(Class: "d-flex gap-2 flex-wrap align-items-center mb-2")[
                    // Headless: we render our own buttons; the triggers just supply the gesture attribute.
                    FullscreenTrigger(g =>
                        Button(Type: "button", Class: "btn btn-primary btn-sm", Id: "fullscreen-btn", Data: g)[
                            "Enter fullscreen"]),
                    EyeDropperTrigger(
                        OnColor: hex =>
                        {
                            _color = hex;
                            StateHasChanged(); // sanctioned pattern for an externally-pushed result
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
                Div(Class: "small text-secondary")[
                    "Both run inside the click gesture, so they work on every host — including Server, where ",
                    Code()["IFullscreen"], " / ", Code()["IEyeDropper"],
                    " can't be injected. The eyedropper needs a Chromium browser; fullscreen is near-universal."]
            ]
        ];
}
