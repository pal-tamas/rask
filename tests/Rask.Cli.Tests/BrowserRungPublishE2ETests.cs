using Rask.Cli.Scaffolding;
using Xunit;

namespace Rask.Cli.Tests;

/// <summary>
///     Does <c>rask new --wasm</c> actually publish a browser bundle into the server's output?
/// </summary>
/// <remarks>
///     <para>
///         Nothing else can answer that. The one-project build only runs on <c>publish</c>, and what it
///         does is generate a second project carrying a different SDK and drive it in another process —
///         so a build gate, a unit test over the generated file, and the scaffolding tests all pass
///         whether or not a single byte of WebAssembly is produced.
///     </para>
///     <para>
///         <b>It publishes twice.</b> The failure that motivated it only appears the second time: the
///         companion used to publish into its own project directory, which made each publish an input to
///         the next one — the bundle's <c>main.js</c> and <c>dotnet.js</c> came back as candidate
///         scoped-JS and failed the build. A gate that publishes once would have been green for it, and
///         would have read as covering this.
///     </para>
/// </remarks>
public sealed class BrowserRungPublishE2ETests
{
    [SkippableFact]
    public async Task The_browser_bundle_reaches_the_publish_output()
    {
        Skip.IfNot(CliBuildE2E.Enabled, CliBuildE2E.SkipReason);

        var (feed, version) = await CliBuildE2E.LocalFeed.Value;

        const string name = "RBrowserRung";
        var temp = Path.Combine(Path.GetTempPath(), "rask-browser-rung", Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(temp, name);

        try
        {
            // Wasm alone: the batteries are irrelevant here and each one costs build time.
            var result = ProjectGenerator.GenerateServer(
                projectDir, name, new ServerBatteries { Wasm = true }, version);

            var fs = new SystemFileSystem();
            foreach (var file in result.Files)
            {
                fs.CreateDirectory(Path.GetDirectoryName(file.Path)!);
                fs.WriteAllText(file.Path, file.Content);
            }

            CliBuildE2E.WriteNuGetConfig(fs, projectDir, feed);

            var csproj = Path.Combine(projectDir, name + ".csproj");
            var publishDir = Path.Combine(temp, "published");

            var (exit, output) = await CliBuildE2E.RunDotnet(
                $"publish \"{csproj}\" -c Release -o \"{publishDir}\" -m:1 -nodeReuse:false");
            Assert.True(exit == 0, $"the first publish failed.{CliBuildE2E.Diagnostics(output)}");

            // The boot module the server-rendered page points at with data-rask-wasm. Its absence is a
            // page that asks for a bundle that is not there, logs a console error, and stays server-live
            // for ever — working, and never doing the thing it was configured for.
            var mainJs = Path.Combine(publishDir, "wwwroot", "main.js");
            Assert.True(File.Exists(mainJs), $"the browser bundle's boot module is absent: {mainJs}");

            // UNFINGERPRINTED, and that is the assertion rather than an incidental path. A takeover boots
            // from a server-rendered page, which carries no import map — so a fingerprinted bundle
            // resolves this to a hashed name that exists in a build output and not in a publish. It works
            // locally and 404s in production.
            var dotnetJs = Path.Combine(publishDir, "wwwroot", "_framework", "dotnet.js");
            Assert.True(
                File.Exists(dotnetJs),
                $"{dotnetJs} is absent — the runtime was published under a content-hashed name, which the "
                + "server-rendered page has no import map to resolve.");

            // The companion compiled the app's own sources: its assembly is named after the app.
            var appAssembly = Path.Combine(publishDir, "wwwroot", "_framework", name + ".Browser.wasm");
            Assert.True(File.Exists(appAssembly), $"the app's own code is not in the bundle: {appAssembly}");

            // Again, without cleaning. See the remark above — this is the run that used to fail.
            var (exit2, output2) = await CliBuildE2E.RunDotnet(
                $"publish \"{csproj}\" -c Release -o \"{publishDir}\" -m:1 -nodeReuse:false");
            Assert.True(
                exit2 == 0,
                "the SECOND publish failed, which is the shape this whole case exists for: the first one "
                + $"leaves output that the next one must not treat as input.{CliBuildE2E.Diagnostics(output2)}");
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); }
            catch (IOException) { /* best effort */ }
            catch (UnauthorizedAccessException) { /* best effort */ }
        }
    }

    [SkippableFact]
    public async Task The_bundle_carries_the_client_transport_and_the_server_does_not()
    {
        Skip.IfNot(CliBuildE2E.Enabled, CliBuildE2E.SkipReason);

        var (feed, version) = await CliBuildE2E.LocalFeed.Value;

        const string name = "RBrowserCqrs";
        var temp = Path.Combine(Path.GetTempPath(), "rask-browser-cqrs", Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(temp, name);

        try
        {
            // Wasm AND cqrs — the arrangement `rask new --wasm` produces by default, and the one the
            // scaffolding tests can only describe. Whether RaskBrowserPackageReference actually restores,
            // and whether BrowserStartup.cs compiles against a package the server half never sees, is a
            // question only a real publish answers.
            var result = ProjectGenerator.GenerateServer(
                projectDir, name, new ServerBatteries { Wasm = true, Cqrs = true }, version);

            var fs = new SystemFileSystem();
            foreach (var file in result.Files)
            {
                fs.CreateDirectory(Path.GetDirectoryName(file.Path)!);
                fs.WriteAllText(file.Path, file.Content);
            }

            CliBuildE2E.WriteNuGetConfig(fs, projectDir, feed);

            var csproj = Path.Combine(projectDir, name + ".csproj");
            var publishDir = Path.Combine(temp, "published");

            var (exit, output) = await CliBuildE2E.RunDotnet(
                $"publish \"{csproj}\" -c Release -o \"{publishDir}\" -m:1 -nodeReuse:false");
            Assert.True(exit == 0, $"the publish failed.{CliBuildE2E.Diagnostics(output)}");

            // The browser half got the client transport. Without this the bundle's AddRaskCqrsClient()
            // call would not have compiled, so reaching here at all is most of the proof — but assert it,
            // because a trimmer that dropped the assembly would leave a bundle that cannot dispatch.
            var framework = Path.Combine(publishDir, "wwwroot", "_framework");
            Assert.True(
                Directory.EnumerateFiles(framework, "Rask.Cqrs.Client.*").Any(),
                "the client transport is not in the bundle, so the browser half cannot dispatch anything: "
                + framework);

            // And the SERVER half did not. This is what RaskBrowserPackageReference exists for: one
            // project means one reference list, and a plain PackageReference would ship endpoint-CALLING
            // code into the very process that answers those endpoints — the arrangement these two
            // packages were split up to prevent.
            Assert.False(
                Directory.EnumerateFiles(publishDir, "Rask.Cqrs.Client.*").Any(),
                "the client transport reached the server's own output, so the browser-only reference is "
                + "not keeping the two halves apart.");

            // The endpoint half is the server's, and belongs exactly where the client does not.
            Assert.True(
                Directory.EnumerateFiles(publishDir, "Rask.Cqrs.Server.*").Any(),
                "the server cannot answer a dispatch: its endpoint half is missing from the output.");
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); }
            catch (IOException) { /* best effort */ }
            catch (UnauthorizedAccessException) { /* best effort */ }
        }
    }
}
