namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class InputFileExample : Component
{
    private int _chosen;

    protected override Component? Render() =>
        Div.Class("grid max-w-sm gap-6")[
            Ui.Input.Of<string>().Type(InputType.File).Label("Logo").OnFiles(files => { _chosen = files.Count; }),
            Ui.Input.Of<string>().Type(InputType.File).Label("Attachments").Multiple().OnFiles(files => { _chosen = files.Count; }),
            Ui.Text[$"Files chosen last: {_chosen}."]
        ];
}
