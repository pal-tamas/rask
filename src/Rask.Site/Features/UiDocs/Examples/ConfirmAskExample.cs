namespace Rask.Site.Features.UiDocs.Examples;

// Asked from code, after a check: only a period that overlaps one already saved is asked about. The words and the
// primary Yes are this question's own; the dialog is still the one this tab's layout places.
public sealed partial class ConfirmAskExample : Component
{
    private readonly HashSet<string> _saved = new(StringComparer.Ordinal) { "March" };
    private string _state = "March is saved.";

    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-center gap-4")[
            Ui.Button.Id("uc-ask-march").OnClick(() => Save("March"))["Save March"],
            Ui.Button.Id("uc-ask-april").OnClick(() => Save("April"))["Save April"],
            Ui.Text.Id("uc-ask-state")[_state]
        ];

    private async Task Save(string month)
    {
        if (_saved.Contains(month)
            && !await Ui.Confirm.Heading("Overlapping period").Yes("Save anyway").Primary()
                .Ask($"{month} overlaps a period already saved. Save it anyway?"))
        {
            _state = $"{month} was not saved.";
            return;
        }

        _saved.Add(month);
        _state = $"{month} is saved.";
    }
}
