namespace Rask.Site.Features;

public sealed partial class BindTimingDemo : Component
{
    private static readonly string[] Taken = ["Atlantis", "El Dorado"];

    private readonly ItineraryModel _model = new();
    private string? _saved;

    protected override Component? Render() =>
    [
        Form.Model(_model).OnSubmit(m => _saved = $"Saved: {m.Name}").Class("flex flex-col gap-3")[
            Ui.Input.Bind(() => _model.Name).Label("Destination")
                .Id("bt-name")
                .Description("Checked when you stop typing.")
                .Debounce(300.Milliseconds)
                .Validate(DestinationName.Validate)
                .Validate(NameIsFree),
            Ui.Textarea.Bind(() => _model.Notes).Label("Notes")
                .Id("bt-notes")
                .Description("Checked when you leave the field.")
                .Blur()
                .Validate(notes => notes.Length <= 40 ? [] : ["Keep the notes under 40 characters."]),
            Ui.Text.Id("bt-model")[$"The model holds “{_model.Name}”."],
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
