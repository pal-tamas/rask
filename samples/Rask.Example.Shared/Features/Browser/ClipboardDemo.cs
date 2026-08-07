using Rask.Core.Browser;

namespace Rask.Example.Shared.Features;

/// <summary><see cref="IClipboard" /> — copy to and read back from the system clipboard.</summary>
public sealed partial class ClipboardDemo(IClipboard clipboard) : Component
{
    private string _input = "Copied from Rask!";
    private string? _read;
    private string? _status;

    protected override Component? Render() =>
        BsCard(Class: Bs.Join(Shadow.Sm, Border.None))[
            BsCardBody()[
                BsInputGroup(Size: BsSize.Sm, Class: "mb-2")[
                    BsInput<string>(Id: "clipboard-input", Value: _input, OnChange: v => _input = v),
                    BsButton(Color: BsColor.Primary, Id: "clipboard-copy", OnClickAsync: Copy)["Copy"],
                    BsButton(Color: BsColor.Primary, Outline: true, Id: "clipboard-paste", OnClickAsync: Paste)["Paste"]
                ],
                Div(Class: "small text-secondary")["Pasted: ", Code(Id: "clipboard-read-value")[_read ?? "(nothing yet)"]],
                Div(Class: "small text-secondary")["Status: ", Code(Id: "clipboard-status")[_status ?? "(idle)"]]
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
