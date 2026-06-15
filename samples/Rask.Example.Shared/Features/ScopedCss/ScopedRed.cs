namespace Rask.Example.Shared.Features;

public sealed class ScopedRed : Component
{
    protected override RenderResult Render() =>
        Div(Class: "box")[
            Span(Class: "dot"),
            "I think .box should be red."
        ];
}
