using Rask.Core;

namespace Rask.Testing.Fixture.UnsetApp;

public sealed partial class App : Component
{
    protected override Component? Render() => Router;
}
