using Rask.Cli.Scaffolding;

namespace Rask.Cli.E2E.Tests;

/// <summary>
///     A scaffolded WebAssembly app published in Release carries no WebAssembly debugger.
/// </summary>
/// <remarks>
///     The Debug build of a Rask WebAssembly client records where the SDK's debug proxy is, so a host serving it can
///     answer <c>/_framework/debug</c> in Development (<c>Rask.Wasm.targets</c>, <c>_RaskWriteDebugProxyPath</c>). A
///     publish must not: not the proxy, not the SDK's dev server it lives beside, and not the path file that would let
///     the host start one. Asserted on a real publish of both templates that have a browser half, because the condition
///     that keeps it out is an MSBuild property only a publish sets.
/// </remarks>
public sealed class ReleasePublishDebuggerE2ETests
{
    [Theory]
    [InlineData("wasm")]
    [InlineData("wasm-hosted")]
    public async Task A_release_publish_carries_no_webassembly_debugger(string template)
    {
        Assert.SkipUnless(CliBuildE2E.Enabled, CliBuildE2E.SkipReason);
        var (feed, version) = await CliBuildE2E.LocalFeed.Value;
        var name = "RNoDebugger" + template.Replace("-", "", StringComparison.Ordinal);
        var temp = Path.Combine(Path.GetTempPath(), "rask-no-debugger", Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(temp, name);
        var publishDir = Path.Combine(temp, "published");

        try
        {
            var result = template == "wasm"
                ? ProjectGenerator.GenerateWasm(projectDir, name, pwa: false, docker: false, version, new ServerBatteries())
                : ProjectGenerator.GenerateWasmHosted(projectDir, name, new ServerBatteries(), version);
            var fs = new SystemFileSystem();
            foreach (var file in result.Files)
            {
                fs.CreateDirectory(Path.GetDirectoryName(file.Path)!);
                fs.WriteAllText(file.Path, file.Content);
            }

            CliBuildE2E.WriteNuGetConfig(fs, projectDir, feed);

            var (exit, output) = await CliBuildE2E.RunDotnet(
                $"publish \"{Path.Combine(projectDir, name + ".csproj")}\" -c Release -o \"{publishDir}\" -m:1 -nodeReuse:false");

            Assert.True(exit == 0, $"[{template}] publish failed.{CliBuildE2E.Diagnostics(output)}");
            Assert.True(
                Directory.Exists(Path.Combine(publishDir, "wwwroot", "_framework")),
                $"[{template}] the publish carries no _framework, so it published no browser app to look in.");
            Assert.Empty(DebuggerFreePublish.DebuggerFilesIn(publishDir));
        }
        finally
        {
            CliBuildE2E.TryDeleteDirectory(temp);
        }
    }
}
