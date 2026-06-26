using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

/// <summary>Browser APIs section — <see cref="StorageEstimateDemo" /> (<c>IStorageEstimator</c>).</summary>
[Route("browser/storage-estimate")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class StorageEstimatePage : Component
{
    protected override RenderResult Head => Title()["Quota estimate — Browser APIs — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Quota estimate",
            "Read the origin's storage quota and usage via IStorageEstimator (navigator.storage.estimate) — "
            + "to budget a cache or warn before filling up. Works on both transports; figures are coarse."),
        CodeSample(
            ["StorageEstimateDemo.cs"],
            Notes: "EstimateAsync() returns a StorageEstimate (Quota/Usage bytes + UsageRatio) via the "
                + "__raskApi.storageEstimate helper, or null where unsupported.",
            Result: StorageEstimateDemo())
    ];
}
