using Rask.Core.Browser;

namespace Rask.Example.Shared.Features;

/// <summary><see cref="IClipboard" /> — copy to and read back from the system clipboard.</summary>
public sealed partial class ClipboardDemo(IClipboard clipboard) : Component
{
    private string _input = "Copied from Rask!";
    private string? _read;
    private string? _status;

    protected override Component? Render() =>
        Div.Class($"{Tw.Card} shadow-sm border-0")[
            Div.Class(Tw.CardBody)[
                Div.Class("mb-2 flex gap-2")[
                    Input.Value(_input).Class(Tw.Input).Id("clipboard-input").OnInput(v => _input = v),
                    Button.Type("button").Class(Tw.BtnPrimary).Id("clipboard-copy").OnClickAsync(Copy)["Copy"],
                    Button.Type("button").Class(Tw.BtnOutlinePrimary).Id("clipboard-paste").OnClickAsync(Paste)["Paste"]
                ],
                Div.Class("text-sm text-slate-500 dark:text-slate-400")["Pasted: ", Code.Id("clipboard-read-value")[_read ?? "(nothing yet)"]],
                Div.Class("text-sm text-slate-500 dark:text-slate-400")["Status: ", Code.Id("clipboard-status")[_status ?? "(idle)"]]
            ]
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
