namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ModalScrollExample : Component
{
    protected override Component? Render() =>
        Div[
            Ui.ModalTrigger.Name("demo-terms")[Ui.Button["Terms"]],
            Ui.Modal.Name("demo-terms").Scroll(Ui.ModalScroll.Body).Class("md:w-lg")[
                Div.Class("space-y-6")[
                    Ui.Heading.Level(2).Lg["Terms of service"],
                    Enumerable.Range(1, 24)
                        .Select(n => Ui.Text.Key(n)[$"{n}. The whole layer scrolls, not a box inside it."])
                        .ToArray(),

                    Div.Class("flex")[Ui.Spacer, Ui.ModalClose[Ui.Button.Primary["Accept"]]]
                ]
            ]
        ];
}
