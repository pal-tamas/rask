using Rask.Core.Browser;

namespace Rask.Example.Shared.Features;

/// <summary>
///     <c>Shareable</c> (Rask.Core) — headless share: it hands <b>your</b> element the
///     <c>data-rask-share</c> attribute, so the click opens the OS share sheet from <b>any</b> host, the
///     Server included. The shared client fires <c>navigator.share</c> inside the gesture (no round-trip, so
///     the activation isn't lost). For a code-driven share on the in-process host, inject <c>IShare</c> from
///     <c>Rask.Client.Browser</c>.
/// </summary>
public sealed partial class ShareDemo : Component
{
    protected override Component? Render() =>
        BsCard.Class(Bs.Join(Shadow.Sm, Border.None))[
            BsCardBody[
                BsStack.Gap(2).WrapItems(true).Class(Margin.Bottom(2))[
                    // Headless: we render our own button; Shareable just supplies the share attribute.
                    Shareable
                        .Data(new ShareData
                        {
                            Title = "Rask",
                            Text = "Build web apps in C# — one component model, server or WebAssembly.",
                            Url = "https://github.com/pal-tamas/rask"
                        })
                        .Template(share => Button
                            .Type("button")
                            .Class("btn btn-primary btn-sm")
                            .Id("share-btn")
                            .Data(share)["Share this page"])
                ],
                Div.Class("small text-secondary")[
                    "Works on every host — the click fires ", Code["navigator.share"],
                    " inside the gesture (so it works on Server too, where an imperative round-trip would lose "
                    + "the activation). Unsupported browsers (e.g. desktop Firefox) no-op."]
            ]
        ];
}
