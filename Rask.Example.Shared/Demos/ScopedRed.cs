using Rask.Core;
using static Rask.Core.Tags;

namespace Rask.Example.Shared;

public sealed class ScopedRed : Component
{
    protected override string? Css => """
        .box {
            background: #fdecec;
            color: #8a1f1f;
            border: 1px solid #f4c2c2;
            padding: 0.7rem 1rem;
            border-radius: 0.5rem;
            font-weight: 600;
            display: flex;
            align-items: center;
            gap: 0.5rem;
        }
        .dot {
            width: 0.6rem;
            height: 0.6rem;
            background: #d23030;
            border-radius: 50%;
        }
        """;

    public override Component Render() =>
        Div(Class: "box", Children:
        [
            Span(Class: "dot"),
            "I think .box should be red."
        ]);
}
