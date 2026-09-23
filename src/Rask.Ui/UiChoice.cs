namespace Rask;

/// <summary>
/// The markup a list of choices is drawn with, shared by <see cref="UiRadioGroup{T}" /> and
/// <see cref="UiCheckboxGroup{T}" />.
/// </summary>
/// <remarks>
/// <para>
/// A radio list and a checkbox list are the same control asked a different question — one answer or several —
/// so everything except the input's type and what "chosen" means is the same, and writing it twice would be
/// two lists that drifted apart on the day one of them grew a description.
/// </para>
/// <para>
/// Every layout keeps a REAL <c>&lt;input&gt;</c> inside a <c>&lt;label&gt;</c>. A card, a pill and a segment
/// look like buttons, but a button is not a choice: the browser's own grouping, the arrow keys inside a radio
/// group, the space bar, the form post and every assistive technology all come from the input being there. The
/// look is `has-[:checked]:` rules on the label around it, which is CSS reading the input's own state — no
/// script, and nothing to keep in sync.
/// </para>
/// </remarks>
internal static class UiChoice
{
    // The container. A radio group is a radiogroup to assistive tech; a checkbox group is a plain group, since
    // ARIA has no "checkboxgroup" and `group` is what carries the name.
    internal static string ContainerClass(Ui.ChoiceLayout layout) => layout switch
    {
        Ui.ChoiceLayout.Cards => "grid gap-2 sm:grid-cols-2",
        Ui.ChoiceLayout.Pills or Ui.ChoiceLayout.Buttons => "flex flex-wrap gap-2",
        Ui.ChoiceLayout.Segmented => "join",
        _ => "flex flex-col gap-1",
    };

    // The label around one choice. Each a complete literal, for the reason UiClassNames exists: a name built by
    // concatenation is invisible to Tailwind's scan, and the choice would render with no styling at all while
    // the build stayed green.
    internal static string ChoiceClass(Ui.ChoiceLayout layout) => layout switch
    {
        Ui.ChoiceLayout.Cards =>
            "flex cursor-pointer items-start gap-3 rounded-box border border-base-300 p-3 "
            + "has-[:checked]:border-primary has-[:checked]:bg-primary/5 "
            + "has-[:disabled]:cursor-not-allowed has-[:disabled]:opacity-50",
        Ui.ChoiceLayout.Pills =>
            "flex cursor-pointer items-center gap-2 rounded-full border border-base-300 px-3 py-1.5 text-sm "
            + "has-[:checked]:border-primary has-[:checked]:bg-primary has-[:checked]:text-primary-content "
            + "has-[:disabled]:cursor-not-allowed has-[:disabled]:opacity-50",
        Ui.ChoiceLayout.Buttons =>
            "flex cursor-pointer items-center gap-2 rounded-btn border border-base-300 px-3 py-1.5 text-sm "
            + "has-[:checked]:border-primary has-[:checked]:bg-primary has-[:checked]:text-primary-content "
            + "has-[:disabled]:cursor-not-allowed has-[:disabled]:opacity-50",
        Ui.ChoiceLayout.Segmented =>
            "join-item flex cursor-pointer items-center gap-2 border border-base-300 px-3 py-1.5 text-sm "
            + "has-[:checked]:border-primary has-[:checked]:bg-primary has-[:checked]:text-primary-content "
            + "has-[:disabled]:cursor-not-allowed has-[:disabled]:opacity-50",
        _ => "flex cursor-pointer items-start gap-3 py-1 has-[:disabled]:cursor-not-allowed "
             + "has-[:disabled]:opacity-50",
    };

    // Whether the input itself is seen. In a card or a list it is — it is the affordance. In a pill, a button
    // or a segment the whole label is the affordance, so the box would be a second one saying the same thing;
    // it is taken out of the picture but NOT out of the accessibility tree or the tab order, which
    // `display: none` or `hidden` would do.
    internal static bool ShowsBox(Ui.ChoiceLayout layout) =>
        layout is Ui.ChoiceLayout.List or Ui.ChoiceLayout.Cards;

    internal const string HiddenBoxClass = "sr-only";
}
