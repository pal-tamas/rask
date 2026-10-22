namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class InputKbdExample : Component
{
    protected override Component? Render() =>
        Ui.Input.Of<string>().Kbd("⌘K").Icon(Ui.IconName.MagnifyingGlass).Placeholder("Search...").Class("max-w-sm");
}
