namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class InputReadonlyExample : Component
{
    protected override Component? Render() =>
        Ui.Input.Value("BA7K7QZ511S8Z2K").ReadOnly().Filled.Class("max-w-sm");
}
