namespace Rask.Example.Shared.Features;

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
            Div.Class("md:col-span-7")[
                Form.Model(_model).OnValidSubmitAsync(SaveAsync).Id("fss-form")[submitting => [
                    Label.Class($"{Ui.Label} font-semibold")["Username"],
                    Input.Bind(() => _model.Username)
                        .Class($"{Ui.Input} mb-2")
                        .Disabled(submitting)
                        .Placeholder("Pick a name…")
                        .Id("fss-input"),
                    Button.Type("submit")
                        .Class(Ui.BtnPrimary)
                        .Disabled(submitting)
                        .Id("fss-submit")[submitting ? "Saving…" : "Sign up"]
                ]]
            ],
            Div.Class("md:col-span-5")[
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
