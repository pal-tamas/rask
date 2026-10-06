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

    private async Task<IKeyValueStore> Store() => _store ??= await indexedDb.OpenStore("rask-demo");

    protected override Component? Render() =>
        Ui.Card.Class("shadow-sm")[
                Div.Class("grid grid-cols-12 gap-4 mb-2")[
                    Div.Class("col-span-12 sm:col-span-4")[
                        Ui.Input
                            .Value(_key)
                            .Label("Key")
                            .Id("idb-key")
                            .OnInput(v => _key = v)],
                    Div.Class("col-span-12 sm:col-span-8")[
                        Ui.Input
                            .Value(_value)
                            .Label("Value")
                            .Id("idb-value")
                            .OnInput(v => _value = v)]
                ],
                Div.Class("flex gap-2 flex-wrap items-center mb-2")[
                    Ui.Button.Primary.Id("idb-set").OnClick(Set)["Set"],
                    Ui.Button.Id("idb-get").OnClick(Get)["Get"],
                    Ui.Button.Id("idb-keys").OnClick(Keys)["List keys"],
                    Ui.Button.Red.Id("idb-clear").OnClick(Clear)["Clear"]
                ],
                Div.Class("text-sm text-ui-muted")["Read: ", Code.Id("idb-read")[_read ?? "(none)"]],
                Div.Class("text-sm text-ui-muted")["Keys: ", Code.Id("idb-keys-value")[_keys ?? "(none)"]],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("idb-status")[_status ?? "(idle)"]]
            ];

    private async Task Set()
    {
        try { await (await Store()).Set(_key, _value); _status = "Stored"; }
        catch (Exception ex) { _status = "Failed: " + ex.Message; }
    }

    private async Task Get()
    {
        try { _read = await (await Store()).Get(_key) ?? "(not found)"; _status = "Read"; }
        catch (Exception ex) { _status = "Failed: " + ex.Message; }
    }

    private async Task Keys()
    {
        try { _keys = string.Join(", ", await (await Store()).Keys()); _status = "Listed"; }
        catch (Exception ex) { _status = "Failed: " + ex.Message; }
    }

    private async Task Clear()
    {
        try { await (await Store()).Clear(); _read = _keys = null; _status = "Cleared"; }
        catch (Exception ex) { _status = "Failed: " + ex.Message; }
    }
}
