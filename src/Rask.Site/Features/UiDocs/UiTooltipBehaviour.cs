namespace Rask.Site.Features.UiDocs;

/// <summary>
///     How a tooltip behaves: its look and placement, what opens it, what a screen reader hears, and the button's
///     own shorthand for one.
/// </summary>
internal sealed partial class UiTooltipBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiDocsBehaviour[
            Look(),
            Opening(),
            Announced(),
            OnAButton()
        ];

    private static UiDocsTopic Look() =>
        UiDocsTopic.Key("look").Heading("Look and placement").Paragraphs([
            "Measured on fluxui.dev, in light and dark, by `scripts/flux/parity-tooltip.mjs`: zinc-800 with white 12px "
            + "text (zinc-700 inside a hairline in dark), 5px from its trigger, no arrow, no shadow, no transition and "
            + "no delay. One look, with no tones.",
            "The content is a `popover` placed by CSS anchor positioning, and the runtime keeps it inside the viewport "
            + "as Flux's script does (`data-rask-place`): it flips to the other side at the viewport's edge, keeps its "
            + "side where it can and slides along the trigger to stay 5px inside, and is held to the height that is "
            + "left. `Gap(12)` and `Offset(20)` are pixels away from the trigger and along its side; an offset slides "
            + "it away from the edge it is aligned to, so with `End` it runs the other way.",
            "The wrapper around the trigger lays out as `inline-flex`, as Flux's does, so a display written in "
            + "`Class` wins: `.Class(\"contents\")` takes it out of a flex row's layout, which is what an editor's "
            + "toolbar does.",
        ]);

    private static Component Opening() =>
        UiDocsTopic.Key("opening").Heading("What opens it").Paragraphs([
            "The kit ships no script; the runtime shows it. The wrapper carries `data-rask-tooltip=\"<content id>\"`, "
            + "one of the runtime's generic hooks, so every tooltip, whatever its trigger (a button, a link, a `Span`, "
            + "a disabled button), is in the top layer, where no card can clip it, and behaves as Flux's does, step "
            + "for step (`parity-tooltip.mjs` walks both): shown the moment the pointer arrives or keyboard focus does, "
            + "gone the moment the pointer leaves unless that focus is still there, dismissed by Escape, and hidden by "
            + "a press on the trigger until the pointer has left and come back. The pointer cannot rest on it.",
        ])[
            UiDocsKeys.Column("Tooltip").Rows([
                ("Any trigger", "The runtime opens it, on the pointer and on keyboard focus. Escape dismisses it."),
                ("`Toggleable()` around a `<button>`", "The browser opens it (`popovertarget`) on a click; a hover does "
                                                        + "nothing. Escape closes it, and so does a click outside."),
                ("`Toggleable()` around anything else", "Focus on the wrapper, which a tap gives it, shows it: in place, "
                                                         + "not in the top layer. Escape does nothing."),
                ("`Disabled()`", "Never shows. The trigger keeps its description."),
            ]),
            Ui.Text.Class("mt-3")[UiDocsProse.Of(
                "What is left: a `Toggleable()` tooltip around something that is not a `<button>` has no click to "
                + "open it by (no runtime hook toggles a popover from an arbitrary element), and a toggleable button's "
                + "expanded state is the browser's own rather than a written `aria-expanded`. On a page whose scripts "
                + "never run (`@media (scripting: none)`) the stylesheet shows a tooltip in place on `:hover` and "
                + "keyboard focus.")]
        ];

    private static UiDocsTopic Announced() =>
        UiDocsTopic.Key("announced").Heading("What a screen reader hears").Paragraphs([
            "The kit wires the trigger to the tooltip the way Flux's script does, at render: a trigger with text of "
            + "its own gets `aria-describedby`, and one without (an icon button) gets `aria-labelledby`, so the "
            + "tooltip is its name and it needs no `AccessibleLabel`.",
            "`Interactive()` says the content is more than a description: the trigger gets `aria-controls` instead, "
            + "with `aria-expanded` `false` at rest and `true` while it shows, and the content stays in the reading "
            + "order. A `Toggleable()` tooltip is Flux's dropdown to a screen reader: its button gets `aria-controls` "
            + "and `aria-haspopup`, and the content is no `role=\"tooltip\"`.",
            "This needs the trigger to be ONE element: an HTML element (`Button[…]`, `A[…]`, `Span[…]`) or a kit "
            + "component that is one, like `Ui.Button`. Around anything else nothing is wired, as Flux wires nothing "
            + "but the trigger: use an element. A tooltip cannot be reached by touch at all unless it is "
            + "`Toggleable()`, so it is a hint, never the only place something is said.",
        ]);

    private static UiDocsTopic OnAButton() =>
        UiDocsTopic.Key("button").Heading("On a button").Paragraphs([
            "`Ui.Button.Icon(Ui.IconName.Cog6Tooth).Tooltip(\"Settings\")` is Flux's shorthand: the button is wrapped "
            + "in a `Ui.Tooltip` that says this, `TooltipPosition(Ui.TooltipPosition.Bottom)` puts it on another side "
            + "and `TooltipKbd(\"D\")` shows a shortcut after it. It names a button that has only an icon "
            + "(`aria-labelledby`), and the button still fuses inside a `Ui.ButtonGroup`.",
            "A button's own `Kbd(\"esc\")` is another thing: a shortcut drawn inside the button, after its label, "
            + "where `TooltipKbd` and a tooltip's `Kbd` draw it in the tooltip. `Ui.Toggle`, `Ui.EditorButton` and a "
            + "sidebar item collapsed to a rail take a `Tooltip` the same way.",
        ]);
}
