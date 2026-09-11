using System.Text.RegularExpressions;
using Rask.Cli.Scaffolding;
using Xunit;

namespace Rask.Cli.E2E.Tests;

/// <summary>
///     Does <c>rask new --wasm</c> actually publish its browser app into the server's output?
/// </summary>
/// <remarks>
///     <para>
///         Nothing else can answer that. The one-project build generates a second project carrying a
///         different SDK and drives it in another process, so a build gate, a unit test over the generated
///         file, and the scaffolding tests all pass whether or not a single byte of WebAssembly is produced.
///     </para>
///     <para>
///         <b>It publishes twice.</b> The failure that motivated it only appears the second time: a
///         companion publishing into its own project directory makes each publish an input to the next, and
///         a gate that publishes once would have been green for it.
///     </para>
/// </remarks>
public sealed class ClientPublishE2ETests
{
    [SkippableFact]
    public async Task The_browser_app_reaches_the_publish_output()
    {
        Skip.IfNot(CliBuildE2E.Enabled, CliBuildE2E.SkipReason);

        var (feed, version) = await CliBuildE2E.LocalFeed.Value;

        const string name = "RClientApp";
        var temp = Path.Combine(Path.GetTempPath(), "rask-client-app", Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(temp, name);

        try
        {
            // Wasm alone: the batteries are irrelevant here and each one costs build time.
            Scaffold(projectDir, name, new ServerBatteries { Wasm = true }, version, feed);

            var csproj = Path.Combine(projectDir, name + ".csproj");
            var publishDir = Path.Combine(temp, "published");

            var (exit, output) = await CliBuildE2E.RunDotnet(
                $"publish \"{csproj}\" -c Release -o \"{publishDir}\" -m:1 -nodeReuse:false");
            Assert.True(exit == 0, $"the first publish failed.{CliBuildE2E.Diagnostics(output)}");

            var wwwroot = Path.Combine(publishDir, "wwwroot");

            // The page UseRaskSpa serves at every client route, with the SDK's import map filled in. An empty
            // map is a page that cannot resolve the fingerprinted runtime: it boots in a build and not here.
            var index = Path.Combine(wwwroot, "index.html");
            Assert.True(File.Exists(index), $"the browser app's boot page is absent: {index}");
            var page = await File.ReadAllTextAsync(index);
            Assert.Contains("data-rask-root", page, StringComparison.Ordinal);
            Assert.Matches(new Regex(@"<script type=""importmap"">\s*\{"), page);

            Assert.True(File.Exists(Path.Combine(wwwroot, "main.js")), "the boot module is absent.");
            Assert.True(File.Exists(Path.Combine(wwwroot, "rask.wasm.js")), "Rask's runtime module is absent.");

            // The companion compiled the app's own Client/ code: its assembly is named after the app.
            var framework = Path.Combine(wwwroot, "_framework");
            Assert.True(
                Directory.EnumerateFiles(framework, name + ".Client*.wasm").Any(),
                $"the app's own code is not in the bundle: {framework}");

            // Again, without cleaning. See the remark above — this is the run that used to fail.
            var (exit2, output2) = await CliBuildE2E.RunDotnet(
                $"publish \"{csproj}\" -c Release -o \"{publishDir}\" -m:1 -nodeReuse:false");
            Assert.True(
                exit2 == 0,
                "the SECOND publish failed: the first one leaves output that the next one must not treat as "
                + $"input.{CliBuildE2E.Diagnostics(output2)}");
        }
        finally
        {
            Cleanup(temp);
        }
    }

    [SkippableFact]
    public async Task The_bundle_carries_the_client_transport_and_the_server_does_not()
    {
        Skip.IfNot(CliBuildE2E.Enabled, CliBuildE2E.SkipReason);

        var (feed, version) = await CliBuildE2E.LocalFeed.Value;

        const string name = "RClientCqrs";
        var temp = Path.Combine(Path.GetTempPath(), "rask-client-cqrs", Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(temp, name);

        try
        {
            // Wasm AND cqrs — whether RaskClientPackageReference restores, and whether Client/Program.cs
            // compiles against a package the server never sees, only a real publish answers.
            Scaffold(projectDir, name, new ServerBatteries { Wasm = true, Cqrs = true }, version, feed);

            var csproj = Path.Combine(projectDir, name + ".csproj");
            var publishDir = Path.Combine(temp, "published");

            var (exit, output) = await CliBuildE2E.RunDotnet(
                $"publish \"{csproj}\" -c Release -o \"{publishDir}\" -m:1 -nodeReuse:false");
            Assert.True(exit == 0, $"the publish failed.{CliBuildE2E.Diagnostics(output)}");

            var framework = Path.Combine(publishDir, "wwwroot", "_framework");
            Assert.True(
                Directory.EnumerateFiles(framework, "Rask.Cqrs.Client*").Any(),
                "the client transport is not in the bundle, so the browser app cannot dispatch anything: "
                + framework);

            // And the SERVER did not: one project means one reference list, and a plain PackageReference
            // would ship endpoint-CALLING code into the very process that answers those endpoints.
            Assert.False(
                Directory.EnumerateFiles(publishDir, "Rask.Cqrs.Client.*").Any(),
                "the client transport reached the server's own output.");

            Assert.True(
                Directory.EnumerateFiles(publishDir, "Rask.Cqrs.Server.*").Any(),
                "the server cannot answer a dispatch: its endpoint half is missing from the output.");

            AssertResponseStreamingIsOn(publishDir);
        }
        finally
        {
            Cleanup(temp);
        }
    }

    [SkippableFact]
    public async Task The_bundle_can_call_an_API_controller_that_only_the_server_compiles()
    {
        Skip.IfNot(CliBuildE2E.Enabled, CliBuildE2E.SkipReason);

        var (feed, version) = await CliBuildE2E.LocalFeed.Value;

        const string name = "RClientApi";
        var temp = Path.Combine(Path.GetTempPath(), "rask-client-api", Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(temp, name);

        try
        {
            Scaffold(projectDir, name, new ServerBatteries { Wasm = true }, version, feed);

            // The shape that crosses the wire is SHARED, exactly as a CQRS message record is.
            WriteFile(projectDir, "Shared/Pong.cs", $$"""
                namespace {{name}}.Shared;

                public sealed record Pong(string Message);
                """);

            // The controller lives outside Client/ and Shared/, so the companion cannot see it: the only way
            // the browser app gets a client for it is the one the server's generator wrote.
            WriteFile(projectDir, "Server/PingController.cs", $$"""
                using Microsoft.AspNetCore.Mvc;
                using {{name}}.Shared;

                namespace {{name}}.Server;

                [ApiController]
                [Route("api/ping")]
                public sealed class PingController : ControllerBase
                {
                    [HttpGet("{id:int}")]
                    public ActionResult<Pong> Get(int id) => new Pong($"pong-{id}");
                }
                """);

            // Compiled by the browser app only. If the bake did not happen, the companion has no PingClient
            // and this does not compile — a publish that succeeds is the proof.
            WriteFile(projectDir, "Client/ApiCaller.cs", $$"""
                using {{name}}.Server;
                using {{name}}.Shared;

                namespace {{name}}.Client;

                public static class ApiCaller
                {
                    public static System.Threading.Tasks.Task<Pong?> Call(PingClient client) => client.Get(1);
                }
                """);

            var csproj = Path.Combine(projectDir, name + ".csproj");
            var text = await File.ReadAllTextAsync(csproj);

            // Rask.Api hosts and carries the generator; Rask.Api.Client is the runtime the generated client
            // calls. The companion's copy of the runtime is added by the client targets when the baked file
            // exists — deliberately NOT written here, because that is the behaviour under test.
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

            var publishDir = Path.Combine(temp, "published");
            var (exit, output) = await CliBuildE2E.RunDotnet(
                $"publish \"{csproj}\" -c Release -o \"{publishDir}\" -m:1 -nodeReuse:false");

            Assert.True(
                exit == 0,
                "the publish failed, so the browser app could not compile against the baked API client."
                + CliBuildE2E.Diagnostics(output));

            var framework = Path.Combine(publishDir, "wwwroot", "_framework");
            Assert.True(
                Directory.EnumerateFiles(framework, "Rask.Api.Client*").Any(),
                $"the API client runtime is not in the bundle: {framework}");

            // The hosting half is the server's: it carries MVC and the generator.
            Assert.False(
                Directory.EnumerateFiles(framework, "Rask.Api.*").Any(f => !Path.GetFileName(f).StartsWith("Rask.Api.Client", StringComparison.Ordinal)),
                "the server-side API hosting package reached the browser bundle.");
        }
        finally
        {
            Cleanup(temp);
        }
    }

    private static void Scaffold(string projectDir, string name, ServerBatteries batteries, string version, string feed)
    {
        var result = ProjectGenerator.GenerateServer(projectDir, name, batteries, version);

        var fs = new SystemFileSystem();
        foreach (var file in result.Files)
        {
            fs.CreateDirectory(Path.GetDirectoryName(file.Path)!);
            if (file.Bytes is { } bytes)
            {
                File.WriteAllBytes(file.Path, bytes);
            }
            else
            {
                fs.WriteAllText(file.Path, file.Content);
            }
        }

        CliBuildE2E.WriteNuGetConfig(fs, projectDir, feed);
    }

    private static void WriteFile(string projectDir, string relative, string content)
    {
        var full = Path.Combine(projectDir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private static void Cleanup(string temp)
    {
        try { Directory.Delete(temp, recursive: true); }
        catch (IOException) { /* best effort */ }
        catch (UnauthorizedAccessException) { /* best effort */ }
    }

    /// <summary>
    ///     The published boot module must carry <c>System.Net.Http.WasmEnableStreamingResponse: true</c>.
    /// </summary>
    /// <remarks>
    ///     <c>docs/cqrs.md</c> promises a <c>FileDownload</c> comes back headers-first. In the browser that
    ///     needs response streaming, and nothing in this repository sets it — <c>BrowserWasmApp.targets</c>
    ///     defaults it to <c>true</c> (#894) — so it is asserted on the SHIPPED artifact rather than on a
    ///     property: the value belongs to the SDK and can move without a Rask commit.
    /// </remarks>
    private static void AssertResponseStreamingIsOn(string publishDir)
    {
        // The switch reaches the browser through the boot config, which the SDK bakes into a boot module's
        // own text. Every .js under _framework is a candidate — fingerprinted or not — plus any
        // runtimeconfig.json; several files NAME the key and only one carries the value.
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
            $"nothing in the publish output mentions {key}. Searched {searched.Count} file(s) under {publishDir}: "
            + string.Join(", ", searched.Select(Path.GetFileName)));

        Assert.True(
            candidates.Exists(f => enabled.IsMatch(f.Text)),
            $"{key} is not true in the published bundle, so BrowserHttpHandler will buffer a whole FileDownload "
            + $"before the caller sees a byte. Carrying the key: {string.Join(", ", candidates.Select(f => Path.GetFileName(f.File)))}");
    }
}
