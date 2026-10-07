namespace Rask.Site.Features;

public sealed partial class SkipFactoryCounter : Component
{
    private int _count;

    // [SkipFactory] excludes this property from the generated chain.
    // The initializer seeds the cached instance — the call site
    // doesn't have to (and can't) pass Initial through.
    [SkipFactory] public int Initial { get; set; } = 7;

    protected override async Task OnMount() => _count = Initial;

    protected override Component? Render() =>
        Ui.Button.Icon(Ui.IconName.CursorArrowRays).Id("skipfactory-counter").OnClick(() => _count++)[$"Clicks: {_count}"];
}
