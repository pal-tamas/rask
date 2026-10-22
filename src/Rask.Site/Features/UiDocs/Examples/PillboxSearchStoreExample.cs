namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class PillboxSearchStoreExample : Component
{
    private static readonly string[] Skills = ["JavaScript", "TypeScript", "PHP", "Python", "Ruby", "Go", "Rust", "Java", "C#", "Swift"];

    private List<string> _skills = [];
    private string[] _found = Skills;
    private int _searches;

    protected override Component? Render() =>
        Div.Class("max-w-80 space-y-3")[
            Ui.Pillbox.Values(_skills).OnChange(picked => { _skills = [.. picked]; }).Filter(false).Placeholder("Choose skills...").Id("ui-pillbox-store")[
                Ui.PillboxSearch.Placeholder("Search skills...").OnInput(Search).Debounce(250.Milliseconds),
                _found.Select(skill => Ui.PillboxOption.Key(skill)[skill])
            ],
            Ui.Text.Data("testid", "ui-pillbox-store-state")[
                $"Skills: {(_skills.Count == 0 ? "none" : string.Join(", ", _skills))}. Searches: {_searches}."
            ]
        ];

    // The page's answer to the search: a query, here over a list.
    private void Search(string text)
    {
        _searches++;
        _found = [.. Skills.Where(skill => skill.Contains(text, StringComparison.CurrentCultureIgnoreCase))];
    }
}
