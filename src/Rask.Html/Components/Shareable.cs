using System.Text.Json;
using Rask.Core.Browser;

namespace Rask.Html.Components;

/// <summary>
///     Headless share — hands <b>your own</b> markup the <c>data-rask-share</c> attribute so that element's
///     click opens the OS share sheet. No prescribed button, no styling: you render the trigger, this
///     supplies the behaviour. The shared client fires <c>navigator.share</c> <b>inside the click gesture</b>,
///     so it works on <b>every</b> host (the Server included — no round-trip, so the activation survives).
///     Spread the bundle onto any element via its <c>Data</c> prop:
///     <code>
///     Shareable(new ShareData { Title = "Rask", Url = "https://…" },
///         share => Button(Type: "button", Class: "btn btn-primary", Data: share)["Share"])
///     </code>
/// </summary>
/// <remarks>
///     Web Share (<c>navigator.share</c>) is available on mobile Safari, Android Chrome and Edge — not
///     desktop Firefox; an unsupported browser no-ops (feature-detect if you need a fallback). For a
///     <b>code-driven</b> share (a lifecycle hook, after an <c>await</c>) on the in-process WASM host,
///     inject <c>IShare</c> from <c>Rask.Wasm.Browser</c> instead.
/// </remarks>
public sealed partial class Shareable : Component
{
    /// <summary>The content to share (title / text / URL). At least one field should be set.</summary>
    public required ShareData Data { get; set; }

    /// <summary>
    ///     Renders your trigger element, given the attribute bundle to apply to it via the element's
    ///     <c>Data</c> prop. The rendered element's click opens the share sheet.
    /// </summary>
    public required Func<IReadOnlyDictionary<string, string?>, Component> Template { get; set; }

    protected override Component Render() =>
        // Serialized with the trim-safe source-gen context; the client reads data-rask-share on click and
        // fires navigator.share synchronously in the gesture.
        Template!(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["rask-share"] = JsonSerializer.Serialize(Data, RaskBrowserJsonContext.Default.ShareData)
        });
}
