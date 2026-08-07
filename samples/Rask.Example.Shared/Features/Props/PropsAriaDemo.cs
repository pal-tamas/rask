namespace Rask.Example.Shared.Features;

public sealed partial class PropsAriaDemo : Component
{
    // Role and TabIndex are typed; Aria is a dictionary that expands to aria-* exactly like Data
    // expands to data-* — so the whole ARIA vocabulary is reachable without a property per attribute.
    protected override Component? Render() =>
        Button(Class: "btn btn-outline-primary", Role: "switch", TabIndex: 0, Aria: new Dictionary<string, string?> { ["label"] = "Toggle dark mode", ["pressed"] = "false" })[
            BsIcon(Name: BsIconName.MoonStars, Class: "me-1"),
            "Theme"];
}
