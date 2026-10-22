namespace Rask.Site.Features;

public sealed partial class PropsAttributeOrderDemo : Component
{
    protected override Component? Render() =>
        A
            .Href("/tags")
            .Id("out")
            .Class("font-medium text-fx-accent-content underline underline-offset-4")
            .Data("external", "true")["See HTML order"];
}
