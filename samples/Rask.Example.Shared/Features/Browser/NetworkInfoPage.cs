using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

/// <summary>Browser APIs section — <see cref="NetworkInfoDemo" /> (<c>INetworkInfo</c>).</summary>
[Route("browser/network")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class NetworkInfoPage : Component
{
    protected override RenderResult Head => Title()["Network info — Browser APIs — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Network info",
            "Read connection quality via INetworkInfo — effective type, downlink, RTT, and Data Saver — to "
            + "adapt loading. Works on both transports; supported on Chromium browsers."),
        CodeSample(
            ["NetworkInfoDemo.cs"],
            Notes: "navigator.connection is read through the framework's __raskApi.network helper; "
                + "GetStatusAsync returns null where the API is unsupported (Firefox/Safari).",
            Result: NetworkInfoDemo())
    ];
}
