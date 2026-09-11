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
        UiCard.Class("shadow-sm")[
                Div.Class("mb-2 flex gap-2")[
                    UiInput.Value(_input).AccessibleLabel("Value to persist")
                        .Id("storage-input")
                        .Placeholder("Value to persist")
                        .OnInput(v => _input = v),
                    UiButton.Tone(UiTone.Primary).Id("storage-set").OnClick(Set)["Set"],
                    UiButton.Tone(UiTone.Primary).Variant(UiVariant.Outline).Id("storage-read").OnClick(Read)["Read"],
                    UiButton.Tone(UiTone.Error).Variant(UiVariant.Outline).Id("storage-remove").OnClick(Remove)["Remove"]
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
