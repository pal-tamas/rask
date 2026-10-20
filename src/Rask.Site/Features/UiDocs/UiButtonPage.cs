using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's button and button group as a C# component: its examples and its reference.</summary>
[Route("button")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiButtonPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Button.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Button)
            .Lead("A button is a <button>, or an <a> given Href. Ui.Button.Primary.Sm.Blue is a variant, a size "
                  + "and a colour, each a step; an icon is a step too, so the button pads itself around it.")
            .Files(["UiKitButtonDemo.cs"])
            .Api(UiApi.Button)
            .Demo(UiKitButtonDemo)
            .Notes("The loading button and the stepper keep their counts in plain fields. A handler still running "
                   + "after 200 ms shows the spinner; nothing on the page asks for it.");
}
