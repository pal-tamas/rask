
namespace Rask.Site.Features;

/// <summary>MDN's <c>localStorage</c>, from Rask.Web — a round-trip, identical on Server and WASM.</summary>
public sealed partial class StorageDemo : Component
{
    private const string StorageKey = "rask.browser.storage";

    private string _input = string.Empty;
    private string? _read;
    private string? _status;

    protected override Component? Render() =>
        Ui.Card[
                Div.Class("mb-2 flex gap-2")[
                    Ui.Input.Value(_input).Label("Value to persist")
                        .Id("storage-input")
                        .Placeholder("Value to persist")
                        .OnInput(v => _input = v),
                    Ui.Button.Primary.Id("storage-set").OnClick(Set)["Set"],
                    Ui.Button.Id("storage-read").OnClick(Read)["Read"],
                    Ui.Button.Red.Id("storage-remove").OnClick(Remove)["Remove"]
                ],
                Div.Class("text-sm text-ui-muted")["Last read: ", Code.Id("storage-read-value")[_read ?? "(null)"]],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("storage-status")[_status ?? "(idle)"]]
            ];

    private async Task Set()
    {
        try
        {
            await LocalStorage.SetItem(StorageKey, _input);
            _status = $"Stored: {_input}";
        }
        catch (Exception ex) { _status = "Set failed: " + ex.Message; }
    }

    private async Task Read()
    {
        try
        {
            _read = await LocalStorage.GetItem(StorageKey);
            _status = $"Read (localStorage holds {await LocalStorage.Length} key(s))";
        }
        catch (Exception ex) { _status = "Read failed: " + ex.Message; }
    }

    private async Task Remove()
    {
        try
        {
            await LocalStorage.RemoveItem(StorageKey);
            _read = null;
            _status = "Removed";
        }
        catch (Exception ex) { _status = "Remove failed: " + ex.Message; }
    }
}
