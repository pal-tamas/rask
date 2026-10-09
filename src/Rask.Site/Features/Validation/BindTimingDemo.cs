namespace Rask.Site.Features;

public sealed partial class BindTimingDemo : Component
{
    private static readonly string[] Taken = ["Atlantis", "El Dorado"];

    private readonly ItineraryModel _model = new();
    private string? _saved;

    protected override Component? Render() =>
    [
        Form.Model(_model).OnSubmit(m => _saved = $"Saved: {m.Name}, for {m.Traveller}").Class("flex flex-col gap-4")[
            Div.Class("grid gap-4 md:grid-cols-3")[
                Ui.Input.Bind(() => _model.Traveller).Label("Traveller")
                    .Id("bt-traveller")
                    .Description("Sent and checked with Save.")
                    .Validate(name => name.Length > 0 ? [] : ["Who is travelling?"]),
                Ui.Input.Bind(() => _model.Name).Label("Destination")
                    .Id("bt-name")
                    .Description("Sent and checked as you type.")
                    .Live()
                    .Validate(DestinationName.Validate)
                    .Validate(NameIsFree),
                Ui.Textarea.Bind(() => _model.Notes).Label("Notes")
                    .Id("bt-notes")
                    .Description("Sent and checked when you leave it.")
                    .Blur()
                    .Validate(notes => notes.Length <= 40 ? [] : ["Keep the notes under 40 characters."])
            ],
            Ui.Text.Id("bt-model")[$"The model holds “{_model.Traveller}”, “{_model.Name}” and “{_model.Notes}”."],
            Div[
                Ui.Button.Primary.Icon(Ui.IconName.CheckCircle).Submit["Save"]
            ]
        ],
        _saved is null
            ? null
            : Ui.Callout.Success.Icon(Ui.IconName.CheckCircle).Class("mt-3").Role("status").Text(_saved)
    ];

    // Stands in for a lookup. It is asked only about a name the value object accepted.
    private static async ValueTask<IEnumerable<string>> NameIsFree(string name)
    {
        await Task.Delay(150, Current.Cancellation);

        return Taken.Contains(name, StringComparer.OrdinalIgnoreCase) ? [$"“{name}” is already taken."] : [];
    }
}
