namespace Rask.Core.Tests.Live;

internal sealed class StubComponent : Component
{
    private readonly Func<Component> _factory;

    public StubComponent(Component root) : this(() => root) { }
    public StubComponent(Func<Component> factory) => _factory = factory;

    protected override Component Render() => _factory();
}
