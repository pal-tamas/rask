using System.Text.RegularExpressions;
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

            AssertResponseStreamingIsOn(publishDir);
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); }
            catch (IOException) { /* best effort */ }
            catch (UnauthorizedAccessException) { /* best effort */ }
        }
    }

    [SkippableFact]
    public async Task The_bundle_can_call_an_API_controller_that_only_the_server_compiles()
    {
        Skip.IfNot(CliBuildE2E.Enabled, CliBuildE2E.SkipReason);

        var (feed, version) = await CliBuildE2E.LocalFeed.Value;

        const string name = "RBrowserApi";
        var temp = Path.Combine(Path.GetTempPath(), "rask-browser-api", Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(temp, name);

        try
        {
            var result = ProjectGenerator.GenerateServer(
                projectDir, name, new ServerBatteries { Wasm = true }, version);

            var fs = new SystemFileSystem();
            foreach (var file in result.Files)
            {
                fs.CreateDirectory(Path.GetDirectoryName(file.Path)!);
                fs.WriteAllText(file.Path, file.Content);
            }

            // The controller lives under Server/, which the companion does not compile. That is the
            // whole point: the browser half cannot see this file, so the only way it can end up with a
            // client for it is the baked one the server's generator wrote.
            // The shape that crosses the wire is SHARED — outside Server/ — exactly as a CQRS message
            // record is. Only the handler is server-only. Put it under Server/ and the generated client
            // returns a type the bundle cannot see, which is a compile error inside generated code.
            fs.WriteAllText(
                Path.Combine(projectDir, "Pong.cs"),
                $$"""
                namespace {{name}};

                public sealed record Pong(string Message);
                """);

            fs.CreateDirectory(Path.Combine(projectDir, "Server"));
            fs.WriteAllText(
                Path.Combine(projectDir, "Server", "PingController.cs"),
                $$"""
                using Microsoft.AspNetCore.Mvc;

                namespace {{name}}.Server;

                [ApiController]
                [Route("api/ping")]
                public sealed class PingController : ControllerBase
                {
                    [HttpGet("{id:int}")]
                    public ActionResult<Pong> Get(int id) => new Pong($"pong-{id}");
                }
                """);

            // A file compiled by BOTH halves that calls the generated client. If the bake did not
            // happen, the companion has no PingClient and this does not compile — which is the
            // assertion. A publish that succeeds is the proof.
            fs.WriteAllText(
                Path.Combine(projectDir, "ApiCaller.cs"),
                $$"""
                using {{name}}.Server;

                namespace {{name}};

                public static class ApiCaller
                {
                    public static System.Threading.Tasks.Task<Pong?> Call(PingClient client) =>
                        client.Get(1);
                }
                """);

            // Both halves need the client runtime the generated code calls. The server half gets it
            // from the PackageReference below; the companion's copy is added by the browser targets
            // when the baked file exists, which is the behaviour under test.

            var csproj = Path.Combine(projectDir, name + ".csproj");
            var text = await File.ReadAllTextAsync(csproj);

            // Rask.Api hosts and carries the generator (server half); Rask.Api.Client is the runtime the
            // generated client calls, and the server half needs it too because a component that calls
            // its own API renders on both. The companion's copy is added by the browser targets when the
            // baked file exists — deliberately NOT written here, because that is the behaviour under test.
            text = text.Replace(
                "</Project>",
                $"""
                   <ItemGroup>
                     <PackageReference Include="Rask.Api" Version="{version}" />
                     <PackageReference Include="Rask.Api.Client" Version="{version}" />
                   </ItemGroup>
                 </Project>
                 """);
            await File.WriteAllTextAsync(csproj, text);

            CliBuildE2E.WriteNuGetConfig(fs, projectDir, feed);

            var publishDir = Path.Combine(temp, "published");
            var (exit, output) = await CliBuildE2E.RunDotnet(
                $"publish \"{csproj}\" -c Release -o \"{publishDir}\" -m:1 -nodeReuse:false");

            Assert.True(
                exit == 0,
                "the publish failed, so the browser half could not compile against the baked API client."
                + CliBuildE2E.Diagnostics(output));

            // The client runtime reached the bundle. Without it the generated client's calls into
            // ApiCall would have nothing to bind to.
            var framework = Path.Combine(publishDir, "wwwroot", "_framework");
            Assert.True(
                Directory.EnumerateFiles(framework, "Rask.Api.Client.*").Any(),
                $"the API client runtime is not in the bundle: {framework}");

            // The hosting half is the server's. It carries MVC and the generator, and the browser has
            // no use for either — shipping it would put the endpoint-answering side in the bundle.
            Assert.False(
                Directory.EnumerateFiles(framework, "Rask.Api.dll").Any(),
                "the server-side API hosting package reached the browser bundle.");
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); }
            catch (IOException) { /* best effort */ }
            catch (UnauthorizedAccessException) { /* best effort */ }
        }
    }

    /// <summary>
    ///     The published boot module must carry <c>System.Net.Http.WasmEnableStreamingResponse: true</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>docs/cqrs.md</c> promises a <c>FileDownload</c> comes back headers-first, and
    ///         <c>RemoteDispatch</c> asks for exactly that with <c>ResponseHeadersRead</c>. In the browser
    ///         neither is enough on its own: <c>BrowserHttpHandler</c> buffers the whole response unless
    ///         response streaming is enabled, so with it off the export the docs call constant-memory is
    ///         materialised whole in the tab.
    ///     </para>
    ///     <para>
    ///         Nothing in this repository sets it — <c>BrowserWasmApp.targets</c> defaults it to
    ///         <c>true</c>, and grepping Rask's own sources for it finds nothing (#894). That is precisely
    ///         why it is asserted on the SHIPPED artifact rather than on a property: the value belongs to
    ///         the SDK, so it can move without a Rask commit, and the doc claim would quietly become
    ///         false. The boot module is where the value ends up, and where the runtime reads it.
    ///     </para>
    /// </remarks>
    private static void AssertResponseStreamingIsOn(string publishDir)
    {
        // The switch reaches the browser through the boot config, which the WebAssembly SDK bakes into
        // the boot module's own text rather than leaving a JSON file beside it. So this reads text.
        //
        // Every .js under _framework is a candidate, plus any runtimeconfig.json the publish carries.
        // The glob is deliberately not `dotnet.*.js`: the one-project build publishes the runtime
        // UNFINGERPRINTED (`dotnet.js`, which that pattern does not match) while a stand-alone WASM app
        // fingerprints it (`dotnet.<hash>.js`) — a pattern that fits only one of them passes the other
        // by finding nothing at all. Several files NAME the key (the runtime reads it too) and only one
        // carries the value, so every match is examined rather than the first.
        const string key = "System.Net.Http.WasmEnableStreamingResponse";
        var enabled = new Regex(@"System\.Net\.Http\.WasmEnableStreamingResponse""\s*:\s*""?true""?");

        var framework = Path.Combine(publishDir, "wwwroot", "_framework");
        var searched = Directory.EnumerateFiles(framework, "*.js")
            .Concat(Directory.EnumerateFiles(publishDir, "*.runtimeconfig.json", SearchOption.AllDirectories))
            .ToList();

        var candidates = searched
            .Select(file => (File: file, Text: File.ReadAllText(file)))
            .Where(f => f.Text.Contains(key, StringComparison.Ordinal))
            .ToList();

        Assert.True(
            candidates.Count > 0,
            $"nothing in the publish output mentions {key}, so a FileDownload's memory behaviour in the "
            + $"browser is unknown. Searched {searched.Count} file(s) under {publishDir}: "
            + string.Join(", ", searched.Select(Path.GetFileName)));

        Assert.True(
            candidates.Exists(f => enabled.IsMatch(f.Text)),
            $"{key} is not true in the published bundle, so BrowserHttpHandler will buffer a whole "
            + "FileDownload before the caller sees a byte — which is the opposite of what docs/cqrs.md "
            + $"promises. Carrying the key: {string.Join(", ", candidates.Select(f => Path.GetFileName(f.File)))}");
    }
}
