using Rask.Core.Live;

namespace Rask.Example.Shared.Features;

public sealed class TagsMediaDemo : Component
{
    protected override RenderResult Render() => Img(
        LiveOptions.PathBase + "/img/rask-placeholder.svg",
        "Rask",
        Class: "rounded shadow-sm");
}
