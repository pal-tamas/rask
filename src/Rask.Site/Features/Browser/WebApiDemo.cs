
namespace Rask.Site.Features;

// MDN's own globals, generated from MDN's data by Rask.Web: each chain runs in the browser in one round trip when it
// is awaited, and an object you await is kept until it is disposed of.
public sealed partial class WebApiDemo : Component
{
    private string _read = "";
    private string _stored = "";
    private string _kept = "";
    private string _width = "";
    private string _seen = "";
    private string _lock = "";
    private IAsyncDisposable? _watch;
    private Types.IntersectionObserver? _observer;
    private readonly ElementRef _panel = ElementRef.New();

    protected override Component? Render() =>
        Div[
            Div.Class("flex gap-2 flex-wrap items-center mb-3")[
                Ui.Button.Primary.Id("web-read").OnClick(Read)["Read the browser"],
                Ui.Button.Id("web-store").OnClick(Store)["Round-trip localStorage"],
                Ui.Button.Id("web-keep").OnClick(Keep)["Keep a media query"],
                Ui.Button.Id("web-watch").OnClick(Watch)["Watch the width"],
                Ui.Button.Id("web-observe").OnClick(ObservePanel)["Observe this panel"],
                Ui.Button.Id("web-lock").OnClick(HoldLock)["Hold a lock"]
            ],
            P.Id("web-read-out").Class("text-sm mb-1")[_read],
            P.Id("web-store-out").Class("text-sm mb-1")[_stored],
            P.Id("web-keep-out").Class("text-sm mb-1")[_kept],
            P.Id("web-watch-out").Class("text-sm mb-1")[_width],
            P.Id("web-observe-out").Ref(_panel).Class("text-sm mb-1")[_seen],
            P.Id("web-lock-out").Class("text-sm mb-0")[_lock]
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

    // MediaQueryList's `change` event: the handler runs here each time the page crosses 900px, and re-renders this.
    private async Task Watch()
    {
        _watch ??= await Window.MatchMedia("(min-width: 900px)").OnChange(e => _width = $"{e.Media} now matches: {e.Matches}");
        _width = "Watching (min-width: 900px)";
    }

    // new IntersectionObserver(callback): the entries arrive as data, each time the panel crosses into or out of view.
    private async Task ObservePanel()
    {
        if (_observer is not null)
        {
            return;
        }

        var observer = await IntersectionObserver.Create(entries =>
            _seen = string.Join(", ", entries.Select(e => e.IsIntersecting ? $"In view, {e.IntersectionRatio:P0} of it" : "Out of view")));
        await observer.Observe(_panel);
        _observer = observer;
    }

    // navigator.locks.request: the browser holds the lock until the async handler has finished.
    private async Task HoldLock()
    {
        await Navigator.Locks.Request("rask-web-demo", async lk =>
        {
            _lock = $"Holding {lk?.Name} ({lk?.Mode})";
            await Task.Delay(100);
        });
        _lock += ", then let it go";
    }

    protected override async Task OnUnmount()
    {
        if (_watch is not null)
        {
            await _watch.DisposeAsync();
        }

        if (_observer is not null)
        {
            await _observer.DisposeAsync();
        }
    }

    // Awaiting the MediaQueryList keeps it in the browser; `await using` lets it go.
    private async Task Keep()
    {
        await using var query = await Window.MatchMedia("(min-width: 1px)");
        await using var url = await URL.Create("/docs/guides/web-apis", await Location.Origin);   // new URL(path, base)
        _kept = $"Kept the MediaQueryList for {await query.Media}: matches {await query.Matches}; a URL for {await url.Pathname}";
    }
}
