namespace Rask.Example.Shared.Features;

public sealed partial class PropsAttributeOrderDemo : Component
{
    protected override Component? Render() =>
        A(
            "/tags",
            Id: "out",
            Class: "link link-primary",
            Data: new Dictionary<string, string?> { ["external"] = "true" })["See HTML order"];
}
