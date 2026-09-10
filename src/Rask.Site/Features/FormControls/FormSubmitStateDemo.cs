namespace Rask.Site.Features;

// Children as a FUNCTION of the submit state. `Form.Model(model)[submitting => [ … ]]` is called on
// every render with whether a submit is in flight, so the busy affordance — the disabled input, the
// button that reads "Saving…" — lives in the markup rather than in a bool this component maintains
// beside the model. The flag is raised when the handler starts and cleared when it returns, and the
// form re-renders on both edges, so only an `async` handler is observable in it.
public sealed partial class FormSubmitStateDemo : Component
{
    private readonly Model _model = new();
    private string _saved = "";

    protected override Component? Render() =>
        Div.Class("grid grid-cols-12 gap-4")[
            Div.Class("col-span-12 md:col-span-7")[
                Form.Model(_model).OnValidSubmit(SaveAsync).Id("fss-form")[submitting => [
                    Label.Class($"{Tw.Label} font-semibold")["Username"],
                    Input.Bind(() => _model.Username)
                        .Class($"{Tw.Input} mb-2")
                        .Disabled(submitting)
                        .Placeholder("Pick a name…")
                        .Id("fss-input"),
                    UiButton
                        // .Key(submitting) is a WORKAROUND, not the idiom — see issue #1050. A component's
                        // props are evaluated through a chain entry cached per (component, slot), and the
                        // owning component's state does not change across a submit; only the flag the form
                        // passes in does, so without a key this button keeps its idle label for the whole
                        // submit. It read correctly as a raw element child, which is why nothing caught it
                        // until the showcase moved onto the kit. Delete this line when #1050 is fixed.
                        .Key(submitting)
                        .Label(submitting ? "Saving…" : "Sign up")
                        .Tone(UiTone.Primary)
                        .Type(UiButtonType.Submit)
                        .Disabled(submitting)
                        .Id("fss-submit")
                ]]
            ],
            Div.Class("col-span-12 md:col-span-5")[
                P.Class("text-sm text-slate-500 dark:text-slate-400 mb-0").Id("fss-out")[
                    "Saved: ", Strong[_saved.Length == 0 ? "(nothing yet)" : _saved]
                ]
            ]
        ];

    // Slow on purpose: a synchronous handler returns before there is a frame to paint, so the busy
    // state would never be seen. This stands in for the round trip a real save makes.
    private async Task SaveAsync(Model m)
    {
        await Task.Delay(800).ConfigureAwait(false);
        _saved = m.Username;
    }

    private sealed class Model
    {
        public string Username { get; set; } = "";
    }
}
