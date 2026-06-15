namespace Rask.Example.Shared.Features;

public sealed class PropsIdClassStyleDemo : Component
{
    protected override RenderResult Render() =>
        Div(
            Id: "card-1",
            Class: "card border-primary",
            Style: "padding: 0.6rem 0.8rem;")["Three attributes — id then class then style."];
}
