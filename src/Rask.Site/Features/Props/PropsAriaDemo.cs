namespace Rask.Site.Features;

public sealed partial class PropsAriaDemo : Component
{
    // Every aria-* attribute of the WAI-ARIA spec is a typed step, generated from the spec: a bool for true/false,
    // an enum for a keyword. AriaRole holds the roles; the Aria bag stays for anything the steps do not name.
    protected override Component? Render() =>
        Ui.Button.Icon(Ui.IconName.Moon)
            .Role(AriaRole.Switch)
            .AriaChecked(AriaChecked.False)
            .AriaLabel("Toggle dark mode")["Theme"];
}
