namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ComposerHeaderExample : Component
{
    private bool _attached = true;

    protected override Component? Render() =>
        Ui.Composer.Of<string>().Label("Prompt").LabelSrOnly().Placeholder("How can I help you today?")
            .Header(_attached
                ? Div.Class("relative overflow-hidden rounded-lg border border-zinc-200 dark:border-zinc-700")[
                    Img.Src("/img/avatars/amber.svg").Alt("Profile picture").Class("size-14"),
                    Div.Class("absolute top-0 right-0 p-1")[
                        Ui.Button.Xs.Filled.Icon(Ui.IconName.XMark).Class("rounded-full").Tooltip("Remove picture")
                            .OnClick(() => { _attached = false; })
                    ]
                ]
                : null)
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
