namespace Rask.Example.Shared.Features;

public sealed partial class TagsVoidDemo : Component
{
    protected override Component? Render() => [
        P.Class("mb-2")["Above the rule"],
        Hr,
        P.Class("mb-0")["Below the rule"]
    ];
}
