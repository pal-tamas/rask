using Rask.Core;
using Rask.Core.Forms;

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

/// <summary>
///     An <see cref="Rask.Core.Globalization.IRaskCulture" /> that counts who listens to <c>Changed</c>, so a test can
///     assert a session subscribes on construction and lets go on dispose. <see cref="RaiseChanged" /> switches nothing;
///     it only fires the event a real language switch would.
/// </summary>
public sealed class CountingCulture : Rask.Core.Globalization.IRaskCulture
{
    private Action? _changed;

    public int SubscriberCount => _changed?.GetInvocationList().Length ?? 0;

    public System.Globalization.CultureInfo Culture => System.Globalization.CultureInfo.InvariantCulture;

    public System.Globalization.CultureInfo UICulture => System.Globalization.CultureInfo.InvariantCulture;

    public IReadOnlyList<System.Globalization.CultureInfo> Supported { get; } =
        [System.Globalization.CultureInfo.InvariantCulture];

    public bool IsRightToLeft => false;

    public event Action? Changed
    {
        add => _changed += value;
        remove => _changed -= value;
    }

    public void RaiseChanged() => _changed?.Invoke();

    public Task<bool> SetAsync(System.Globalization.CultureInfo culture) => Task.FromResult(false);

    public Task<bool> SetAsync(string name) => Task.FromResult(false);
}
