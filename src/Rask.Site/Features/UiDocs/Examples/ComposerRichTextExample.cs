namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ComposerRichTextExample : Component
{
    protected override Component? Render() =>
        Ui.Composer.Of<string>().Rows(3).Label("Prompt").LabelSrOnly().Placeholder("How can I help you today?")
            .Input(Ui.Editor.Toolbar("bold italic bullet ordered | link | align"))
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
