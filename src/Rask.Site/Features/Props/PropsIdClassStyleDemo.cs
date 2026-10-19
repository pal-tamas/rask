namespace Rask.Site.Features;

public sealed partial class PropsIdClassStyleDemo : Component
{
    protected override Component? Render() =>
        Ui.Text
            .Id("card-1")
            .Class("rounded-lg ring-2 ring-ui-brand")
            .Style("padding: 0.6rem 0.8rem;")["Three attributes — id then class then style."];
}
