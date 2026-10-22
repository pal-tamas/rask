namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class PillboxSearchableExample : Component
{
    private static readonly string[] Skills = ["JavaScript", "TypeScript", "PHP", "Python", "Ruby", "Go", "Rust", "Java", "C#", "Swift"];

    private List<string> _skills = [];

    protected override Component? Render() =>
        Div.Class("max-w-80 space-y-3")[
            Ui.Pillbox.Values(_skills).OnChange(picked => { _skills = [.. picked]; }).Searchable().SearchPlaceholder("Filter skills...")
                .Placeholder("Choose skills...").Id("ui-pillbox-searchable")[
                Skills.Select(skill => Ui.PillboxOption.Key(skill)[skill])
            ],
            Ui.Text.Data("testid", "ui-pillbox-searchable-state")[_skills.Count == 0 ? "Skills: none." : $"Skills: {string.Join(", ", _skills)}."]
        ];
}
