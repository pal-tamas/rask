using Rask.Core.Browser;

namespace Rask.Site.Features;

/// <summary><see cref="IClipboard" /> — copy to and read back from the system clipboard.</summary>
public sealed partial class ClipboardDemo(IClipboard clipboard) : Component
{
    private string _input = "Copied from Rask!";
    private string? _read;
    private string? _status;

    protected override Component? Render() =>
        UiCard.Class("shadow-sm")[
                Div.Class("mb-2 flex gap-2")[
                    UiInput.Value(_input).AccessibleLabel("Text to copy").Id("clipboard-input").OnInput(v => _input = v),
                    UiButton.Tone(UiTone.Primary).Id("clipboard-copy").OnClick(Copy)["Copy"],
                    UiButton.Tone(UiTone.Primary).Variant(UiVariant.Outline).Id("clipboard-paste").OnClick(Paste)["Paste"]
                ],
                Div.Class("text-sm text-ui-muted")["Pasted: ", Code.Id("clipboard-read-value")[_read ?? "(nothing yet)"]],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("clipboard-status")[_status ?? "(idle)"]]
            ];

    private async Task Copy()
    {
        try
        {
            await clipboard.WriteTextAsync(_input);
            _status = "Copied to clipboard";
        }
        catch (Exception ex) { _status = "Copy failed: " + ex.Message; }
    }

    private async Task Paste()
    {
        try
        {
            _read = await clipboard.ReadTextAsync();
            _status = "Pasted from clipboard";
        }
        catch (Exception ex) { _status = "Paste failed: " + ex.Message; }
    }
}
