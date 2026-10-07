
namespace Rask.Site.Features;

/// <summary>MDN's Clipboard API from Rask.Web — copy to and read back from the system clipboard.</summary>
public sealed partial class ClipboardDemo : Component
{
    private string _input = "Copied from Rask!";
    private string? _read;
    private string? _status;

    protected override Component? Render() =>
        Ui.Card[
                Div.Class("mb-2 flex gap-2")[
                    Ui.Input.Value(_input).Label("Text to copy").Id("clipboard-input").OnInput(v => _input = v),
                    Ui.Button.Primary.Id("clipboard-copy").OnClick(Copy)["Copy"],
                    Ui.Button.Id("clipboard-paste").OnClick(Paste)["Paste"]
                ],
                Div.Class("text-sm text-ui-muted")["Pasted: ", Code.Id("clipboard-read-value")[_read ?? "(nothing yet)"]],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("clipboard-status")[_status ?? "(idle)"]]
            ];

    private async Task Copy()
    {
        try
        {
            await Navigator.Clipboard.WriteText(_input);
            _status = "Copied to clipboard";
        }
        catch (Exception ex) { _status = "Copy failed: " + ex.Message; }
    }

    private async Task Paste()
    {
        try
        {
            _read = await Navigator.Clipboard.ReadText();
            _status = "Pasted from clipboard";
        }
        catch (Exception ex) { _status = "Paste failed: " + ex.Message; }
    }
}
