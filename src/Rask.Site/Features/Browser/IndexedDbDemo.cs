using Rask.Core.Browser;

namespace Rask.Site.Features;

/// <summary>
///     <see cref="IIndexedDb" /> — a persistent, async key/value store backed by IndexedDB (larger than
///     localStorage). Set a value, read it back, and list keys; the data survives a reload.
/// </summary>
public sealed partial class IndexedDbDemo(IIndexedDb indexedDb) : Component
{
    private IKeyValueStore? _store;
    private string _key = "greeting";
    private string _value = "hello from IndexedDB";
    private string? _read;
    private string? _keys;
    private string? _status;

    private async Task<IKeyValueStore> StoreAsync() => _store ??= await indexedDb.OpenStoreAsync("rask-demo");

    protected override Component? Render() =>
        UiCard.Class("shadow-sm")[
                Div.Class("grid grid-cols-12 gap-4 mb-2")[
                    Div.Class("col-span-12 sm:col-span-4")[
                        Input
                            .Value(_key)
                            .Id("idb-key")
                            .Class(Tw.Input)
                            .OnInput(v => _key = v)],
                    Div.Class("col-span-12 sm:col-span-8")[
                        Input
                            .Value(_value)
                            .Id("idb-value")
                            .Class(Tw.Input)
                            .OnInput(v => _value = v)]
                ],
                Div.Class("flex gap-2 flex-wrap items-center mb-2")[
                    UiButton.Label("Set").Tone(UiTone.Primary).Id("idb-set").OnClick(Set),
                    UiButton.Label("Get").Tone(UiTone.Primary).Variant(UiVariant.Outline).Id("idb-get").OnClick(Get),
                    UiButton.Label("List keys").Variant(UiVariant.Outline).Id("idb-keys").OnClick(Keys),
                    UiButton.Label("Clear").Tone(UiTone.Error).Variant(UiVariant.Outline).Id("idb-clear").OnClick(Clear)
                ],
                Div.Class("text-sm text-ui-muted")["Read: ", Code.Id("idb-read")[_read ?? "(none)"]],
                Div.Class("text-sm text-ui-muted")["Keys: ", Code.Id("idb-keys-value")[_keys ?? "(none)"]],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("idb-status")[_status ?? "(idle)"]]
            ];

    private async Task Set()
    {
        try { await (await StoreAsync()).SetAsync(_key, _value); _status = "Stored"; }
        catch (Exception ex) { _status = "Failed: " + ex.Message; }
    }

    private async Task Get()
    {
        try { _read = await (await StoreAsync()).GetAsync(_key) ?? "(not found)"; _status = "Read"; }
        catch (Exception ex) { _status = "Failed: " + ex.Message; }
    }

    private async Task Keys()
    {
        try { _keys = string.Join(", ", await (await StoreAsync()).KeysAsync()); _status = "Listed"; }
        catch (Exception ex) { _status = "Failed: " + ex.Message; }
    }

    private async Task Clear()
    {
        try { await (await StoreAsync()).ClearAsync(); _read = _keys = null; _status = "Cleared"; }
        catch (Exception ex) { _status = "Failed: " + ex.Message; }
    }
}
