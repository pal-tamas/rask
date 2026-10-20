namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class BadgeCloseExample : Component
{
    private readonly List<string> _roles = ["Admin", "Editor", "Billing"];

    protected override Component? Render() =>
        Div.Class("flex flex-wrap gap-2")[
            _roles.Select(role => Ui.Badge.Key(role)[
                role,
                Ui.BadgeClose.Aria("label", "Remove " + role).OnClick(() => { _roles.Remove(role); })
            ])
        ];
}
