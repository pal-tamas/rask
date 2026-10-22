namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ComposerInlineExample : Component
{
    protected override Component? Render() =>
        Ui.Composer.Of<string>().Rows(1).Inline().Label("Prompt").LabelSrOnly().Placeholder("How can I help you today?")
            .ActionsLeading(Ui.Button.Sm.Ghost.Icon(Ui.IconName.Plus))
            .ActionsTrailing([
                Ui.Button.Sm.Filled.Icon(Ui.IconName.Microphone),
                Ui.Button.Type(Ui.ButtonType.Submit).Sm.Primary.Icon(Ui.IconName.PaperAirplane),
            ]);
}
