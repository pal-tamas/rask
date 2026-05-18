namespace Rask.Example.Shared.Demos;

public sealed class ScopedRed : Component
{
    protected override Component Render() =>
        Div(Class: "box")[
            Span(Class: "dot"),
            "I think .box should be red."
        ];
}
