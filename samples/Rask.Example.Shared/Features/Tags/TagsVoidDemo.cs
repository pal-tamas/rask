namespace Rask.Example.Shared.Features;

public sealed class TagsVoidDemo : Component
{
    protected override RenderResult Render() => Fragment()[
        P(Class: "mb-2")["Above the rule"],
        Hr(),
        P(Class: "mb-0")["Below the rule"]
    ];
}
