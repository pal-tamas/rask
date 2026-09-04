namespace Rask.Example.Shared.Features;

public sealed partial class PropsIdClassStyleDemo : Component
{
    protected override Component? Render() =>
        Div
            .Id("card-1")
            .Class($"{Tw.Card} ring-violet-500")
            .Style("padding: 0.6rem 0.8rem;")["Three attributes — id then class then style."];
}
