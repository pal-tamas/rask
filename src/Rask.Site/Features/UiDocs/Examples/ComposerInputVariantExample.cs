namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ComposerInputVariantExample : Component
{
    protected override Component? Render() =>
        Ui.Composer.Of<string>().Variant(Ui.ComposerVariant.Input).Label("Message").Placeholder("What's on your mind?")
            .ActionsLeading([
                Ui.Button.Sm.Subtle.Icon(Ui.IconName.PaperClip),
                Ui.Button.Sm.Subtle.Icon(Ui.IconName.Slash),
                Ui.Button.Sm.Subtle.Icon(Ui.IconName.AdjustmentsHorizontal),
            ])
            .ActionsTrailing(Ui.Button.Sm.Filled.Icon(Ui.IconName.Microphone));
}
