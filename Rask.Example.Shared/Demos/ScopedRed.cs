namespace Rask.Example.Shared.Demos;

public sealed class ScopedRed : Component
{
    protected override RenderResult Render() =>
        Div(Class: "box")[
            Span(Class: "dot"),
            "I think .box should be red."
        ];
}
