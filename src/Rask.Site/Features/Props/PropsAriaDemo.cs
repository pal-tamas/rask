namespace Rask.Site.Features;

public sealed partial class PropsAriaDemo : Component
{
    // Role and TabIndex are typed; Aria is a dictionary that expands to aria-* exactly like Data
    // expands to data-* — so the whole ARIA vocabulary is reachable without a property per attribute.
    protected override Component? Render() =>
        Ui.Button.Tone(Ui.Tone.Primary).Variant(Ui.Variant.Outline)
            .Role("switch")
            .TabIndex(0)
            .Aria(new Dictionary<string, string?> { ["label"] = "Toggle dark mode", ["pressed"] = "false" })[Ui.Icon.Name(Ui.IconName.Moon), "Theme"];
}
