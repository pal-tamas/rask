namespace Rask.Site.Features;

public sealed partial class SkipFactoryCounter : Component
{
    private int _count;

    // [SkipFactory] excludes this property from the generated factory.
    // The initializer seeds the cached instance — the factory call site
    // doesn't have to (and can't) pass Initial through.
    [SkipFactory] public int Initial { get; set; } = 7;

    protected override async Task OnMount() => _count = Initial;

    protected override Component? Render() =>
        Ui.Button.Primary.Outline.Id("skipfactory-counter").OnClick(() => _count++)[Ui.Icon.Name(Ui.IconName.Cursor), $"Clicks: {_count}"];
}
