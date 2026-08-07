namespace Rask.Example.Shared.Features;

/// <summary>
///     Lazy-mounted child. Sibling <c>LazyChild.css</c> is loaded only when this
///     component appears in the rendered tree. DevTools shows the network request happen
///     only when the parent toggles "Show".
/// </summary>
public sealed partial class LazyChild : Component
{
    protected override Component? Render() =>
        Div(Class: "lazy-child")[
            "I just mounted — my CSS was fetched on demand. Toggle me off and back on; the second mount uses the browser's HTTP cache."
        ];
}
