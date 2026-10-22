namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ComposerExample : Component
{
    private readonly Chat _chat = new();
    private string? _sent;

    protected override Component? Render() =>
        Div.Class("space-y-3")[
            Form.Model(_chat).OnSubmit(_ => Send())[
                Ui.Composer.Bind(() => _chat.Prompt).Label("Prompt").LabelSrOnly().Placeholder("How can I help you today?").Id("ui-composer-prompt")
                    .ActionsLeading([
                        Ui.Button.Sm.Subtle.Icon(Ui.IconName.PaperClip),
                        Ui.Button.Sm.Subtle.Icon(Ui.IconName.Slash),
                        Ui.Button.Sm.Subtle.Icon(Ui.IconName.AdjustmentsHorizontal),
                    ])
                    .ActionsTrailing([
                        Ui.Button.Sm.Filled.Icon(Ui.IconName.Microphone),
                        Ui.Button.Type(Ui.ButtonType.Submit).Sm.Primary.Icon(Ui.IconName.PaperAirplane),
                    ])
            ],
            Ui.Text.Data("testid", "ui-composer-sent")[_sent is null ? "Nothing sent yet." : "Sent: " + _sent]
        ];

    private void Send()
    {
        if (_chat.Prompt.Trim().Length > 0)
        {
            _sent = _chat.Prompt;
            _chat.Prompt = "";
        }
    }

    public sealed class Chat
    {
        public string Prompt { get; set; } = "";
    }
}
