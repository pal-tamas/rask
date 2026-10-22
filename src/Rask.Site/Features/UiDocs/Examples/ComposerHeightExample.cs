namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ComposerHeightExample : Component
{
    protected override Component? Render() =>
        Ui.Composer.Of<string>().Rows(4).MaxRows(8).Label("Prompt").LabelSrOnly().Placeholder("How can I help you today?")
            .ActionsLeading([
                Ui.Button.Sm.Subtle.Icon(Ui.IconName.PaperClip),
                Ui.Button.Sm.Subtle.Icon(Ui.IconName.Slash),
                Ui.Button.Sm.Subtle.Icon(Ui.IconName.AdjustmentsHorizontal),
            ])
            .ActionsTrailing([
                Ui.Button.Sm.Filled.Icon(Ui.IconName.Microphone),
                Ui.Button.Type(Ui.ButtonType.Submit).Sm.Primary.Icon(Ui.IconName.PaperAirplane),
            ]);
}
