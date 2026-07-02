namespace Rask.Example.Shared.Features;

public sealed class ScopedBlue : Component
{
    protected override Component? Render() =>
        Div(Class: "box")[
            Span(Class: "dot"),
            "I think .box should be blue."
        ];
}
