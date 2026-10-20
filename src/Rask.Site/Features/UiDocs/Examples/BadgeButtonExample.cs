namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class BadgeButtonExample : Component
{
    private int _amount = 1;

    protected override Component? Render() =>
        Ui.Badge.As(Ui.BadgeAs.Button).Rounded().Icon(Ui.IconName.Plus).Lg
            .OnClick(() => { _amount++; })[$"Amount {_amount}"];
}
