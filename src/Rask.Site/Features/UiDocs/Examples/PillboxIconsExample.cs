namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class PillboxIconsExample : Component
{
    private List<string> _platforms = [];

    protected override Component? Render() =>
        Ui.Pillbox.Values(_platforms).OnChange(picked => { _platforms = [.. picked]; }).Placeholder("Choose platforms...").Class("max-w-80")[
            Platform("github", Ui.IconName.CodeBracket, "GitHub"),
            Platform("gitlab", Ui.IconName.Server, "GitLab"),
            Platform("bitbucket", Ui.IconName.Cloud, "Bitbucket")
        ];

    private static Component Platform(string value, Ui.IconName icon, string name) =>
        Ui.PillboxOption.Key(value).Value(value)[
            Div.Class("flex items-center gap-2")[Ui.Icon.Name(icon).Mini.Class("text-zinc-400"), name]
        ];
}
