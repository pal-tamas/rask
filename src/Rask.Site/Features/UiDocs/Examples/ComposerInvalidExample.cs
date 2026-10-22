using System.ComponentModel.DataAnnotations;

namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ComposerInvalidExample : Component
{
    private readonly Chat _chat = new();

    // Send it empty: the form holds a message for the prompt, and the composer is drawn invalid until it is fixed.
    protected override Component? Render() =>
        Form.Model(_chat).OnSubmit(_ => { _chat.Prompt = ""; })[
            Ui.Composer.Bind(() => _chat.Prompt).Label("Prompt").LabelSrOnly().Placeholder("How can I help you today?").Id("ui-composer-invalid")
                .ActionsLeading([
                    Ui.Button.Sm.Subtle.Icon(Ui.IconName.PaperClip),
                    Ui.Button.Sm.Subtle.Icon(Ui.IconName.Slash),
                    Ui.Button.Sm.Subtle.Icon(Ui.IconName.AdjustmentsHorizontal),
                ])
                .ActionsTrailing([
                    Ui.Button.Sm.Filled.Icon(Ui.IconName.Microphone),
                    Ui.Button.Type(Ui.ButtonType.Submit).Sm.Primary.Icon(Ui.IconName.PaperAirplane),
                ])
        ];

    public sealed class Chat
    {
        [Required(ErrorMessage = "Write a prompt first.")]
        public string Prompt { get; set; } = "";
    }
}
