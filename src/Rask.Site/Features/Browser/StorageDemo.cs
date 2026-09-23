using Rask.Core.Browser;

namespace Rask.Site.Features;

/// <summary>
///     <see cref="IBrowserStorage" /> — a <c>localStorage</c> round-trip, injected through the ctor and
///     identical on Server and WASM.
/// </summary>
public sealed partial class StorageDemo(IBrowserStorage storage) : Component
{
    private const string StorageKey = "rask.browser.storage";

    private string _input = string.Empty;
    private string? _read;
    private string? _status;

    protected override Component? Render() =>
        Ui.Card.Class("shadow-sm")[
                Div.Class("mb-2 flex gap-2")[
                    Ui.Input.Value(_input).AccessibleLabel("Value to persist")
                        .Id("storage-input")
                        .Placeholder("Value to persist")
                        .OnInput(v => _input = v),
                    Ui.Button.Tone(Ui.Tone.Primary).Id("storage-set").OnClick(Set)["Set"],
                    Ui.Button.Tone(Ui.Tone.Primary).Variant(Ui.Variant.Outline).Id("storage-read").OnClick(Read)["Read"],
                    Ui.Button.Tone(Ui.Tone.Error).Variant(Ui.Variant.Outline).Id("storage-remove").OnClick(Remove)["Remove"]
                ],
                Div.Class("text-sm text-ui-muted")["Last read: ", Code.Id("storage-read-value")[_read ?? "(null)"]],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("storage-status")[_status ?? "(idle)"]]
            ];

    private async Task Set()
    {
        try
        {
            await storage.Local.SetAsync(StorageKey, _input);
            _status = $"Stored: {_input}";
        }
        catch (Exception ex) { _status = "Set failed: " + ex.Message; }
    }

    private async Task Read()
    {
        try
        {
            _read = await storage.Local.GetAsync(StorageKey);
            var count = await storage.Local.LengthAsync();
            _status = $"Read (localStorage holds {count} key(s))";
        }
        catch (Exception ex) { _status = "Read failed: " + ex.Message; }
    }

    private async Task Remove()
    {
        try
        {
            await storage.Local.RemoveAsync(StorageKey);
            _read = null;
            _status = "Removed";
        }
        catch (Exception ex) { _status = "Remove failed: " + ex.Message; }
    }
}
