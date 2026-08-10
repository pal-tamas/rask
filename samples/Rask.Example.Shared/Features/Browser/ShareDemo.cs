using Rask.Core.Browser;

namespace Rask.Example.Shared.Features;

/// <summary>
///     <c>Shareable</c> (Rask.Core) — headless share: it hands <b>your</b> element the
///     <c>data-rask-share</c> attribute, so the click opens the OS share sheet from <b>any</b> host, the
///     Server included. The shared client fires <c>navigator.share</c> inside the gesture (no round-trip, so
///     the activation isn't lost), and upgrades to a native backend in the native shell. For a code-driven
///     share on the in-process hosts, inject <c>IShare</c> from <c>Rask.Client.Browser</c>.
/// </summary>
public sealed partial class ShareDemo : Component
{
    protected override Component? Render() =>
        BsCard.Class(Bs.Join(Shadow.Sm, Border.None))[
            BsCardBody[
                BsStack.Gap(2).WrapItems(true).Class(Margin.Bottom(2))[
                    // Headless: we render our own button; Shareable just supplies the share attribute.
                    Shareable(
                        new ShareData
                        {
                            Title = "Rask",
                            Text = "Ship real iOS/Android apps from the same C# component code.",
                            Url = "https://github.com/pal-tamas/rask"
                        },
                        share => Button
                            .Type("button")
                            .Class("btn btn-primary btn-sm")
                            .Id("share-btn")
                            .Data(share)["Share this page"])
                ],
                Div.Class("small text-secondary")[
                    "Works on every host — the click fires ", Code["navigator.share"],
                    " inside the gesture (so it works on Server too, where an imperative round-trip would lose "
                    + "the activation) and upgrades to the native sheet in the native shell. Unsupported "
                    + "browsers (e.g. desktop Firefox) no-op."]
            ]
        ];
}
