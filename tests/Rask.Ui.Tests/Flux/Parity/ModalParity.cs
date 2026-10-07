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
///     What a modal holds is the real thing — headings, text, inputs, buttons — except what is not rebuilt
///     yet: Flux's spacer and its subheading.
///     </para>
/// </remarks>
public sealed partial class ModalParity : FluxParity
{
    // What an app's own Tailwind build emits for the classes Flux's examples hand to `class`, under names of
    // this page's own: unlayered, a real utility's name would beat the kit's `dark:` twin of it.
    internal const string AppUtilities =
        "<style>.parity-min-w-22{min-width:22rem}.parity-mt-2{margin-top:8px}.parity-mb-2{margin-bottom:8px}"
        + "@media (min-width:48rem){.parity-w-96{width:24rem}.parity-w-lg{width:32rem}}</style>";

    public override string Page => "modal";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        // Flux's page puts this first modal inside a <form>, a block of its own, which leaves it no line to sit on.
        yield return ("", Centre(
            Ui.ModalTrigger.Name("edit-profile")[Ui.Button["Edit profile"]],
            Div[Ui.Modal.Name("edit-profile").Class("parity-w-96")[Profile()]]));

        yield return ("confirmation", Centre(
            Ui.ModalTrigger.Name("delete-profile")[Ui.Button.Danger["Delete"]],
            Ui.Modal.Name("delete-profile").Class("parity-min-w-22")[Confirmation()]));

        // The docs' code gives this flyout no width; the example on the page is 32rem wide from md all the same.
        yield return ("flyout", Centre(
            Ui.ModalTrigger.Name("edit-profile-2")[Ui.Button["Edit profile"]],
            Ui.Modal.Name("edit-profile-2").Flyout().Class("parity-w-lg")[Profile()]));

        // Its footer slot, in the docs' code, is not on the page: the example there ends at the second field.
        yield return ("flyout-positioning", Centre(
            Ui.ModalTrigger.Name("edit-profile-4")[Ui.Button["Edit profile"]],
            Ui.Modal.Name("edit-profile-4").Flyout().Floating.Class("parity-w-lg")[FloatingProfile()]));
    }

    // Flux's example frame: a 24rem row, and in it a block the trigger and its modal share.
    internal static Component Centre(params Component[] items) =>
        Div.Style("display:flex;justify-content:center")[Div[Raw.Value(AppUtilities), items]];

    internal static Component Profile() =>
        SpaceY(6,
            Div[Ui.Heading.Lg["Update profile"], Ui.Text.Class("parity-mt-2")["Make changes to your personal details."]],
            Ui.Input.Of<string>().Label("Name").Placeholder("Your name"),
            Ui.Input.Of<string>().Label("Date of birth").Type(InputType.Date),
            Div.Style("display:flex")[Spacer(), Ui.Button.Primary.Type(Ui.ButtonType.Submit)["Save changes"]]);

    internal static Component Confirmation() =>
        SpaceY(6,
            Div[
                Ui.Heading.Lg["Delete project?"],
                Ui.Text.Class("parity-mt-2")["You're about to delete this project.", Br, "This action cannot be reversed."]
            ],
            Div.Style("display:flex;gap:8px")[
                Spacer(),
                Ui.ModalClose[Ui.Button.Ghost["Cancel"]],
                Ui.Button.Danger.Type(Ui.ButtonType.Submit)["Delete project"]
            ]);

    internal static Component FloatingProfile() =>
        SpaceY(6,
            // Flux's subheading, which the kit has not rebuilt: the line it takes, and the 8px Flux's heading
            // keeps above one (its own rule for a heading a subheading follows).
            Ui.Heading.Lg.Class("parity-mb-2")["Update profile"],
            Div.Data("parity-skip", "").Style("height:20px"),
            Ui.Input.Of<string>().Label("Name").Placeholder("Your name"),
            Ui.Input.Of<string>().Label("Date of birth").Type(InputType.Date));

    // Flux's spacer, which the kit's is not yet: the room it takes in a row.
    private static Component Spacer() => Div.Data("parity-skip", "").Style("flex:1 1 0%");
}
