using Rask.Core.Browser;

namespace Rask.Example.Shared.Features;

/// <summary><see cref="IClipboard" /> — copy to and read back from the system clipboard.</summary>
public sealed class ClipboardDemo(IClipboard clipboard) : Component
{
    private string _input = "Copied from Rask!";
    private string? _read;
    private string? _status;

    protected override RenderResult Render() =>
        Div(Class: "card shadow-sm border-0")[
            Div(Class: "card-body")[
                Div(Class: "input-group input-group-sm mb-2")[
                    Input(Id: "clipboard-input", Class: "form-control", Value: _input, OnInput: v => _input = v),
                    Button(Class: "btn btn-primary", Id: "clipboard-copy", OnClickAsync: Copy)["Copy"],
                    Button(Class: "btn btn-outline-primary", Id: "clipboard-paste", OnClickAsync: Paste)["Paste"]
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
