namespace Rask.Example.Shared.Features;

public sealed class PropsAttributeOrderDemo : Component
{
    protected override RenderResult Render() =>
        A(
            "/tags",
            Id: "out",
            Class: "link link-primary",
            Data: new Dictionary<string, string?> { ["external"] = "true" })["See HTML order"];
}
