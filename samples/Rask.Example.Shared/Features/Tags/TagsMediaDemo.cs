using Rask.Core.Live;

namespace Rask.Example.Shared.Features;

public sealed partial class TagsMediaDemo : Component
{
    protected override Component? Render() => Img
        .Src(LiveOptions.PathBase + "/img/rask-placeholder.svg")
        .Alt("Rask")
        .Class("rounded shadow-sm");
}
