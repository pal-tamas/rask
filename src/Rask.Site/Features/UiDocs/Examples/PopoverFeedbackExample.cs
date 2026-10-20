namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class PopoverFeedbackExample : Component
{
    private bool _open;
    private string _kind = "bug";
    private string _text = "";
    private string _sent = "nothing yet";

    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-center gap-4")[
            // The page owns Open, so the form's own buttons can close it.
            Ui.Dropdown.Open(_open).OnToggle(open => { _open = open; })[
                Ui.Button.Icon(Ui.IconName.ChatBubbleOvalLeft)["Feedback"],
                Ui.Popover.Class("flex w-[min(30rem,calc(100vw-2rem))] flex-col gap-4")[
                    Ui.RadioGroup.Value(_kind).Id("popover-kind").Buttons.Class("max-sm:flex-wrap").OnChange(v => { _kind = v; })[
                        Ui.Radio.Value("bug").Icon(Ui.IconName.BugAnt).Class("flex-1")["Bug report"],
                        Ui.Radio.Value("suggestion").Icon(Ui.IconName.LightBulb).Class("flex-1")["Suggestion"],
                        Ui.Radio.Value("question").Icon(Ui.IconName.QuestionMarkCircle).Class("flex-1")["Question"]
                    ],
                    Ui.Textarea.Value(_text).Id("popover-feedback").Rows(8).Class("dark:bg-transparent!")
                        .Placeholder("Please include reproduction steps. You may attach images or video files.")
                        .OnInput(v => { _text = v; }),
                    Div.Class("flex justify-end gap-2")[
                        Ui.Button.Filled.Sm.Kbd("esc").Class("w-28").OnClick(() => { _open = false; })["Cancel"],
                        Ui.Button.Sm.Kbd("⏎").Class("w-28").OnClick(Send)["Submit"]
                    ]
                ]
            ],
            Ui.Text.Data("testid", "ui-popover-sent")[$"Last feedback: {_sent}."]
        ];

    private void Send()
    {
        _sent = _text.Length == 0 ? $"an empty {_kind}" : $"a {_kind}, “{_text}”";
        _text = "";
        _open = false;
    }
}
