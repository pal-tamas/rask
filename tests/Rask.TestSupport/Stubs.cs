using Rask.Core;
using Rask.Core.Components;
using Rask.Core.Forms;
using Rask.Html.Components;

namespace Rask.TestSupport;

/// <summary>
///     Renders whatever component a test supplies. Useful as a live-render root that
///     forwards to the component under test (which often can't itself be a root).
/// </summary>
public sealed partial class StubComponent : Component
{
    private readonly Func<Component> _factory;

    public StubComponent(Component root) : this(() => root) { }
    public StubComponent(Func<Component> factory) => _factory = factory;

    protected override Component? Render() => _factory();
}

/// <summary>
///     Captures the ambient <see cref="EditContext" /> during render so a test can assert
///     against the context a form/validator pushed onto <see cref="EditContextScope" />.
/// </summary>
public sealed partial class ContextCapture(Action<EditContext> capture) : Component
{
    protected override Component? Render()
    {
        if (EditContextScope.Current is { } c)
        {
            capture(c);
        }

        return null;
    }
}
