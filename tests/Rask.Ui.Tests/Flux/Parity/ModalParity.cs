using System.Globalization;
using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>
///     <c>fluxui.dev/components/modal</c>, example for example.
/// </summary>
/// <remarks>
///     <para>
///     Measured twice by <c>scripts/flux/parity-modal.mjs</c>: as loaded, where a modal is its trigger and a
///     dialog nobody sees, and again with each example's trigger pressed — the open dialog is the component.
///     </para>
///     <para>
///     What a modal HOLDS is other components' work — a heading, fields, buttons — so each is a stand-in
///     here (<c>data-parity-skip</c>): a box of the size Flux's takes, compared for where the modal puts it
///     and not for what is in it. The stand-in buttons are real ones, so the triggers and the Cancel work.
///     </para>
/// </remarks>
public sealed partial class ModalParity : FluxParity
{
    // What an app's own Tailwind build emits for the classes these examples hand to Class.
    private const string AppUtilities =
        "<style>.min-w-\\[22rem\\]{min-width:22rem}"
        + "@media (min-width:48rem){.md\\:w-96{width:24rem}.md\\:w-lg{width:32rem}}</style>";

    public override string Page => "modal";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        // Flux's page puts this first modal inside a <form>, a block of its own, which leaves it no line to sit on.
        yield return ("", Centre(
            Raw.Value(AppUtilities),
            Ui.ModalTrigger.Name("edit-profile")[Press("Edit profile", 106.02)],
            Div[
                Ui.Modal.Name("edit-profile").Class("md:w-96")[
                    Div[Room(52, 24), Room(75, 24), Room(75, 24), Room(40)]
                ]
            ]));

        yield return ("confirmation", Centre(
            Ui.ModalTrigger.Name("delete-profile")[Press("Delete", 74.94)],
            Ui.Modal.Name("delete-profile").Class("min-w-[22rem]")[
                Div[
                    Room(72, 24),
                    Div.Style("display:flex;gap:8px")[
                        Div.Data("parity-skip", null).Style("flex:1 1 0%"),
                        Ui.ModalClose[Press("Cancel", 78.48)],
                        Press("Delete project", 125.52)

                    ]
                ]
            ]));

        // The docs' code gives this flyout no width; the example on the page is 32rem wide from md all the same.
        yield return ("flyout", Centre(
            Ui.ModalTrigger.Name("edit-profile-2")[Press("Edit profile", 106.02)],
            Ui.Modal.Name("edit-profile-2").Flyout().Class("md:w-lg")[
                Div[Room(52, 24), Room(76, 24), Room(76, 24), Room(40)]
            ]));

        // Its footer slot, in the docs' code, is not on the page: the example there ends at the second field.
        yield return ("flyout-positioning", Centre(
            Ui.ModalTrigger.Name("edit-profile-4")[Press("Edit profile", 106.02)],
            Ui.Modal.Name("edit-profile-4").Flyout().Floating.Class("md:w-lg")[
                Div[Room(24, 8), Room(20, 24), Room(76, 24), Room(76)]
            ]));
    }

    // Flux's example frame: a 24rem row, and in it a block the trigger and its modal share.
    private static Component Centre(params Component[] items) =>
        Div.Style("display:flex;justify-content:center")[Div[items]];

    // A stand-in for content: as tall as Flux's, with the gap Flux's leaves under it.
    private static Component Room(double height, double gap = 0) =>
        Div.Data("parity-skip", null).Style(string.Create(CultureInfo.InvariantCulture, $"height:{height}px;margin-bottom:{gap}px"));

    // A stand-in for a Flux button, which the kit's button is not yet: the size of Flux's, and a real button.
    private static Component Press(string label, double width) =>
        Button
            .Type(ButtonType.Button)
            .Data("parity-skip", null)
            .Style(string.Create(CultureInfo.InvariantCulture, $"display:inline-flex;align-items:center;justify-content:center;width:{width}px;height:40px"))[
            label
        ];
}

