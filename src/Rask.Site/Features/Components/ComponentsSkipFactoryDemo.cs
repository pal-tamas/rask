namespace Rask.Site.Features;

// The generated factory has NO Initial parameter — the call site stays clean.
// Framework caches the instance by tree position, so _count survives
// re-renders just like any other private state. The counter starts at 7.
public sealed partial class ComponentsSkipFactoryDemo : Component
{
    protected override Component? Render() => SkipFactoryCounter;
}
