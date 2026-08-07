using Rask.Core.Browser;

namespace Rask.Example.Shared.Features;

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
        Div(Class: "card shadow-sm border-0")[
            Div(Class: "card-body")[
                BsRow(Gutter: 2, Class: Margin.Bottom(2))[
                    BsCol(Sm: 4)[
                        Rask.Core.Components.Generated.Input(Id: "idb-key", Class: "form-control form-control-sm", Value: _key, OnInput: v => _key = v)],
                    BsCol(Sm: 8)[
                        Rask.Core.Components.Generated.Input(Id: "idb-value", Class: "form-control form-control-sm", Value: _value,
                            OnInput: v => _value = v)]
                ],
                BsStack(Gap: 2, WrapItems: true, Class: Margin.Bottom(2))[
                    Button(Class: "btn btn-primary btn-sm", Id: "idb-set", OnClickAsync: Set)["Set"],
                    Button(Class: "btn btn-outline-primary btn-sm", Id: "idb-get", OnClickAsync: Get)["Get"],
                    Button(Class: "btn btn-outline-secondary btn-sm", Id: "idb-keys", OnClickAsync: Keys)["List keys"],
                    Button(Class: "btn btn-outline-danger btn-sm", Id: "idb-clear", OnClickAsync: Clear)["Clear"]
                ],
                Div(Class: "small text-secondary")["Read: ", Code(Id: "idb-read")[_read ?? "(none)"]],
                Div(Class: "small text-secondary")["Keys: ", Code(Id: "idb-keys-value")[_keys ?? "(none)"]],
                Div(Class: "small text-secondary")["Status: ", Code(Id: "idb-status")[_status ?? "(idle)"]]
            ]
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
