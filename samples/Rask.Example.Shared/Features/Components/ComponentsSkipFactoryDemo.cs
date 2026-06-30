namespace Rask.Example.Shared.Features;

public sealed class SkipFactoryCounter : Component
{
    private int _count;

    // [SkipFactory] excludes this property from the generated factory.
    // The initializer seeds the cached instance — the factory call site
    // doesn't have to (and can't) pass Initial through.
    [SkipFactory] public int Initial { get; set; } = 7;

    protected override void OnMount() => _count = Initial;

    protected override RenderResult Render() =>
        BsButton(Color: BsColor.Primary, Outline: true, Id: "skipfactory-counter", OnClick: () => _count++)[BsIcon(Name: BsIconName.HandIndex, Class: "me-2"), $"Clicks: {_count}"];
}

// The generated factory has NO Initial parameter — the call site stays clean.
// Framework caches the instance by tree position, so _count survives
// re-renders just like any other private state. The counter starts at 7.
public sealed class ComponentsSkipFactoryDemo : Component
{
    protected override RenderResult Render() => SkipFactoryCounter();
}
