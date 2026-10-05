using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
///     Every scaffolded app references <c>Rask.DevTools</c> directly.
/// </summary>
/// <remarks>
///     A scaffolded app references a host package — the server template names <c>Rask.Server</c>, the
///     wasm template <c>Rask.Wasm</c> — and neither carries the devtools. Without the direct reference, "a Debug build has the
///     devtools" is true only for an app somebody assembled by hand.
/// </remarks>
public sealed class DevToolsTemplateReferenceTests
{
    private const string Root = "/proj/App";
    private const string Version = "9.9.9";

    [Fact]
    public void The_server_template_references_the_devtools()
    {
        var result = ProjectGenerator.GenerateServer(Root, "App", new ServerBatteries(), Version);

        Assert.Contains("Rask.DevTools", result.Packages);
    }

    [Fact]
    public void The_wasm_template_references_the_devtools()
    {
        var result = ProjectGenerator.GenerateWasm(
            Root, "App", pwa: false, docker: false, Version, new ServerBatteries());

        Assert.Contains("Rask.DevTools", result.Packages);
    }
}
