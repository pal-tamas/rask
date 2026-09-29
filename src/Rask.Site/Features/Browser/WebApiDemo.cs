using Rask.Web;

namespace Rask.Site.Features;

// MDN's own globals, generated from MDN's data by Rask.Web: each chain runs in the browser in one round trip when it
// is awaited, and an object you await is kept until it is disposed of.
public sealed partial class WebApiDemo : Component
{
    private string _read = "";
    private string _stored = "";
    private string _kept = "";

    protected override Component? Render() =>
        Div[
            Div.Class("flex gap-2 flex-wrap items-center mb-3")[
                Ui.Button.Tone(Ui.Tone.Primary).Id("web-read").OnClick(Read)["Read the browser"],
                Ui.Button.Variant(Ui.Variant.Outline).Id("web-store").OnClick(Store)["Round-trip localStorage"],
                Ui.Button.Variant(Ui.Variant.Outline).Id("web-keep").OnClick(Keep)["Keep a media query"]
            ],
            P.Id("web-read-out").Class("text-sm mb-1")[_read],
            P.Id("web-store-out").Class("text-sm mb-1")[_stored],
            P.Id("web-keep-out").Class("text-sm mb-0")[_kept]
        ];

    // navigator.language, document.visibilityState, matchMedia(…).matches: three round trips.
    private async Task Read()
    {
        var language = await Navigator.Language;
        var page = await Document.VisibilityState;
        var dark = await Window.MatchMedia("(prefers-color-scheme: dark)").Matches;
        _read = $"Language {language}, page {page}, dark mode {(dark ? "on" : "off")}";
    }

    private async Task Store()
    {
        await LocalStorage.SetItem("rask-web-demo", "stored by Rask.Web");
        _stored = $"localStorage says: {await LocalStorage.GetItem("rask-web-demo")}";
    }

    // Awaiting the MediaQueryList keeps it in the browser; `await using` lets it go.
    private async Task Keep()
    {
        await using var query = await Window.MatchMedia("(min-width: 1px)");
        _kept = $"Kept the MediaQueryList for {await query.Media}: matches {await query.Matches}";
    }
}
