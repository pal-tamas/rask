namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class RadioButtonsExample : Component
{
    private string Feedback { get; set; } = "bug";

    protected override Component? Render() =>
        Ui.RadioGroup.Bind(() => Feedback).Buttons.Class("w-full *:flex-1").Label("Feedback type")[
            Ui.Radio.Value("bug").Icon(Ui.IconName.BugAnt)["Bug report"],
            Ui.Radio.Value("suggestion").Icon(Ui.IconName.LightBulb)["Suggestion"],
            Ui.Radio.Value("question").Icon(Ui.IconName.QuestionMarkCircle)["Question"]
        ];
}
