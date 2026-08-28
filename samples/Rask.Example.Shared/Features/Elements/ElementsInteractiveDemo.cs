namespace Rask.Example.Shared.Features;

// Interactive elements: details/summary (a native disclosure), dialog (shown inline via Open), and
// menu (a semantic command list).
public sealed partial class ElementsInteractiveDemo : Component
{
    protected override Component? Render() => Div.Class("vstack gap-3")[
        Details.Open(true).Class("border rounded p-2")[
            Summary.Class("fw-semibold")["Disclosure — click to toggle"],
            P.Class("mb-0 mt-2 text-secondary")["The browser shows/hides this natively; no JS needed."]
        ],
        // <dialog open> renders in the normal flow (non-modal). showModal() would need JS interop.
        Dialog.Open(true).Class("position-static d-block border rounded p-3 m-0 shadow-sm")[
            P.Class("mb-0")["An open ", Code["<dialog open>"], " — non-modal, rendered in flow."]
        ],
        Div[
            P.Class("small mb-1 text-secondary")["menu (a semantic toolbar / command list)"],
            Menu.Class("list-inline mb-0")[
                Li.Class("list-inline-item")[Button.Type("button").Class(Ui.BtnOutlineSecondary)["Cut"]],
                Li.Class("list-inline-item")[Button.Type("button").Class(Ui.BtnOutlineSecondary)["Copy"]],
                Li.Class("list-inline-item")[Button.Type("button").Class(Ui.BtnOutlineSecondary)["Paste"]]
            ]
        ]
    ];
}
