namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SelectLoadingExample : Component
{
    private static readonly (int Id, string Name)[] Projects =
        [(1, "Branding"), (2, "Analytics"), (3, "Infrastructure"), (4, "Documentation")];

    private string _search = "";
    private int? _projectId;

    protected override Component? Render() =>
        Ui.Select.Value(_projectId).OnChange(value => { _projectId = value; }).Combobox.Filter(false).Class("max-w-sm")[
            Ui.SelectInput.Placeholder("Start typing...").OnInput(Search).Debounce(250.Milliseconds),
            Projects
                .Where(project => project.Name.Contains(_search, StringComparison.CurrentCultureIgnoreCase))
                .Select(project => Ui.SelectOption.Key(project.Id).Value(project.Id)[project.Name]),
            Ui.SelectOptionEmpty.WhenLoading("Loading projects...")["No projects found."]
        ];

    // A slow store: long enough to read the loading message.
    private async Task Search(string text)
    {
        await Task.Delay(600.Milliseconds);
        _search = text;
    }
}
