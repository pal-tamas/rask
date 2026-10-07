namespace Rask.Site.Features;

// Element refs end to end: a ref typed to the element's MDN interface carries that interface's DOM members, generated
// from MDN (Focus, GetBoundingClientRect), and any ref still hands the element to scoped TypeScript. The refs are
// fields so their ids stay stable across renders.
public sealed partial class ElementRefDemo : Component
{
    private readonly ElementRef<HTMLInputElement> _input = new();
    private readonly ElementRef<HTMLElement> _box = new();
    private readonly ElementRef<HTMLDialogElement> _dialog = new();
    private string _measured = "";

    protected override Component? Render() =>
        Div[
            Ui.Input.Of<string>().Label("Focus me from C#")
                .Type(InputType.Text)
                .Placeholder("Focus me from C#")
                .Ref(_input).Class("mb-2"),
            Div.Class("flex gap-2 flex-wrap items-center mb-3")[
                Ui.Button.Primary.OnClick(FocusInput)["Focus the input"],
                Ui.Button.OnClick(MeasureBox)["Measure the box"],
                Ui.Button.OnClick(MeasureInJs)["Measure it in TypeScript"],
                Ui.Button.OnClick(OpenDialog)["Open the dialog"]
            ],
            Div.Ref(_box).Class("border rounded p-3 bg-ui-well")[
                "A box carrying an ElementRef — measured from C# through MDN's getBoundingClientRect, or in TypeScript."
            ],
            Dialog.Ref(_dialog).Class("rounded-box p-4")[
                P.Class("mb-3")["Opened with MDN's showModal(), from C#."],
                Ui.Button.OnClick(CloseDialog)["Close"]
            ],
            _measured.Length > 0
                ? P.Class("text-sm text-ui-muted mt-2 mb-0")[_measured]
                : null
        ];

    // MDN's HTMLDialogElement.showModal(), then its open attribute read back from the live element.
    private async Task OpenDialog()
    {
        await _dialog.ShowModal();
        _measured = $"Dialog open: {await _dialog.Open}";
    }

    private async Task CloseDialog()
    {
        await _dialog.Close();
        _measured = $"Dialog open: {await _dialog.Open}";
    }

    // MDN's HTMLElement.focus(), on the live input.
    private async Task FocusInput() => await _input.Focus();

    // MDN's Element.getBoundingClientRect(), returned as a DOMRect record.
    private async Task MeasureBox()
    {
        var rect = await _box.GetBoundingClientRect();
        _measured = $"Box width: {rect.Width:F0}px (MDN's getBoundingClientRect, from C#)";
    }

    // User scoped TS: Width is generated from ElementRefDemo.ts's `export function width`, and the ref
    // resolves to the element before the script sees it.
    private async Task MeasureInJs()
    {
        var width = await Width(_box);
        _measured = $"Box width: {width:F0}px (measured in TypeScript from the passed element)";
    }
}
