using Microsoft.JSInterop;

namespace Rask.Example.Shared.Features;

// Demonstrates element refs end to end: a built-in (FocusAsync) and a hand-off to user scoped
// JS (ElementRefDemo.js receives the resolved DOM element to measure it). The refs are fields so
// their ids stay stable across renders.
public sealed partial class ElementRefDemo : Component
{
    private readonly ElementRef _box = ElementRef.New();
    private readonly ElementRef _input = ElementRef.New();
    private readonly IJSRuntime _js;
    private string _measured = "";

    public ElementRefDemo(IJSRuntime js) => _js = js;

    protected override Component? Render() =>
        Div[
            Input.Value<string>(null)
                .Type(InputType.Text)
                .Class($"{Ui.Input} mb-2")
                .Placeholder("Focus me from C#")
                .Ref(_input),
            Div.Class("flex gap-2 flex-wrap items-center mb-3")[
                Button.Type("button").Class(Ui.BtnPrimary).OnClickAsync(FocusInput)["Focus the input"],
                Button.Type("button").Class(Ui.BtnOutlineSecondary).OnClickAsync(MeasureBox)["Measure the box"]
            ],
            Div.Ref(_box).Class("border rounded p-3 bg-slate-100")[
                "A box carrying an ElementRef — its width is read by passing the ref to JS."
            ],
            _measured.Length > 0
                ? P.Class("text-sm text-slate-500 dark:text-slate-400 mt-2 mb-0")[_measured]
                : null
        ];

    // Built-in helper: passes the ref to __raskEl.focus, which receives the resolved element.
    private async Task FocusInput() => await _input.FocusAsync(_js);

    // User scoped JS: the ref resolves to the element before width() is called with it.
    private async Task MeasureBox()
    {
        var width = await _js.InvokeAsync<double>("Rask.ElementRefDemo.width", _box);
        _measured = $"Box width: {width:F0}px (measured in JS from the passed element)";
    }
}
