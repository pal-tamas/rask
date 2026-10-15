namespace Rask.Site.Features.UiKit;

// Flux UI's composer, example by example: a message box in a form. Cmd/Ctrl + Enter (or Enter, where the
// example says so) sends the form in the browser, and what was typed travels with that submit.
public sealed partial class UiKitDataInputDemo
{
    private const string Ask = "How can I help you today?";

    private readonly Conversation _chat = new();

    private Component ComposerSection() =>
        Section(
            "Composer — Flux UI's",
            "A message box with buttons around it. It sends the form it is in on Cmd+Enter or Ctrl+Enter — on Enter "
            + "with .Enter — and grows a line at a time to MaxRows (ten unless set); typing sends nothing until then. "
            + "An editor put in its Input slot stands in for the box, bound to the same value.",
            Div.Data(Testid("ui-composer")).Class("grid max-w-3xl items-start gap-8 sm:grid-cols-2")[
                Div.Key("send").Class("space-y-3")[
                    Form.Model(_chat).OnSubmit(_ => Send(_chat.Prompt))[
                        Ui.Composer.Bind(() => _chat.Prompt).Id("composer-prompt").Label("Prompt").LabelSrOnly().Placeholder(Ask)
                            .ActionsLeading(Tools())
                            .ActionsTrailing([
                                Ui.Button.Sm.Filled.Icon(Ui.IconName.Microphone),
                                Ui.Button.Type(Ui.ButtonType.Submit).Sm.Primary.Icon(Ui.IconName.PaperAirplane),
                            ])
                    ],
                    P.Class("text-sm text-ui-muted").Data(Testid("ui-composer-sent"))[
                        _chat.Sent.Count == 0 ? "Nothing sent yet." : $"Sent: {_chat.Sent[^1]}"
                    ]
                ],
                Ui.Composer.Of<string>().Key("header").Placeholder(Ask)
                    .Header(Div.Class("relative overflow-hidden rounded-lg border border-zinc-200 dark:border-zinc-700")[
                        Img.Src("/img/rask-mark.svg").Alt("Attached picture").Class("size-14")
                    ])
                    .ActionsLeading(Tools())
                    .ActionsTrailing(Outgoing()),
                Ui.Composer.Of<string>().Key("inline").Rows(1).Inline().Placeholder(Ask)
                    .ActionsLeading(Ui.Button.Sm.Ghost.Icon(Ui.IconName.Plus))
                    .ActionsTrailing(Outgoing()),
                Ui.Composer.Of<string>().Key("input").Variant(Ui.ComposerVariant.Input).Label("Message").Placeholder("What's on your mind?")
                    .ActionsLeading(Tools())
                    .ActionsTrailing(Ui.Button.Sm.Filled.Icon(Ui.IconName.Microphone)),
                Ui.Composer.Of<string>().Key("height").Rows(4).MaxRows(8).Placeholder(Ask)
                    .ActionsLeading(Tools())
                    .ActionsTrailing(Outgoing()),
                Form.Model(_chat).Key("enter").OnSubmit(_ => Send(_chat.Quick))[
                    Ui.Composer.Bind(() => _chat.Quick).Id("composer-enter").Label("Quick message").LabelSrOnly().Enter.Placeholder(Ask)
                        .ActionsLeading(Tools())
                        .ActionsTrailing(Outgoing())
                ],
                Ui.Composer.Of<string>().Key("rich").Rows(3).Placeholder(Ask)
                    .Input(Ui.Editor.Toolbar("bold italic bullet ordered | link | align"))
                    .ActionsLeading(Tools())
                    .ActionsTrailing(Outgoing()),
                Ui.Composer.Of<string>().Key("disabled").Placeholder(Ask).Disabled()
                    .ActionsLeading(Tools())
                    .ActionsTrailing(Outgoing()),
                Ui.Composer.Of<string>().Key("invalid").Label("Message").LabelSrOnly().Placeholder(Ask).Invalid()
                    .ActionsLeading(Tools())
                    .ActionsTrailing(Outgoing())
            ]);

    private void Send(string text)
    {
        if (text.Trim().Length == 0)
        {
            return;
        }

        _chat.Sent.Add(text);
        _chat.Prompt = string.Empty;
        _chat.Quick = string.Empty;
    }

    private static Component Tools() =>
    [
        Ui.Button.Sm.Subtle.Icon(Ui.IconName.PaperClip),
        Ui.Button.Sm.Subtle.Icon(Ui.IconName.Slash),
        Ui.Button.Sm.Subtle.Icon(Ui.IconName.AdjustmentsHorizontal),
    ];

    private static Component Outgoing() =>
    [
        Ui.Button.Sm.Filled.Icon(Ui.IconName.Microphone),
        Ui.Button.Type(Ui.ButtonType.Submit).Sm.Primary.Icon(Ui.IconName.PaperAirplane),
    ];

    private sealed class Conversation
    {
        public string Prompt { get; set; } = string.Empty;

        public string Quick { get; set; } = string.Empty;

        public List<string> Sent { get; } = [];
    }
}
