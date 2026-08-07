namespace Rask.Example.Shared.Features;

public sealed partial class ScopedBlue : Component
{
    protected override Component? Render() =>
        Div(Class: "box")[
            Span(Class: "dot"),
            "I think .box should be blue."
        ];
}
