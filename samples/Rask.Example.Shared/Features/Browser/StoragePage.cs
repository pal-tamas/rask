using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

/// <summary>Browser APIs section — <see cref="StorageDemo" /> (<c>IBrowserStorage</c>).</summary>
[Route("browser/storage")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class StoragePage : Component
{
    protected override RenderResult Head => Title()["Storage — Browser APIs — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Storage",
            "localStorage / sessionStorage via IBrowserStorage — typed, awaitable, identical on both transports."),
        CodeSample(
            ["StorageDemo.cs"],
            Notes: "Inject IBrowserStorage and use .Local / .Session. Each method is a thin await over IJSRuntime.",
            Result: StorageDemo())
    ];
}
