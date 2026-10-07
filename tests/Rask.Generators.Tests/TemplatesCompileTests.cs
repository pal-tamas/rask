using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Api.Generators;
using Rask.Batteries.Generators;
using Rask.Cli.Scaffolding;
using Rask.Generators.Blazor;
using Rask.Generators.External;
using Rask.Generators.Validation;

namespace Rask.Generators.Tests;

/// <summary>
///     What <c>rask new</c> writes compiles against the framework as it is in this tree.
/// </summary>
/// <remarks>
///     The templates are in no project the solution builds, so a public rename used to leave every scaffold
///     broken with all the fast gates green; only the CLI build E2E, minutes in, said so. This compiles the
///     materialised C# in memory with the real generators. It proves the sources bind to today's public
///     surface. It does not prove restore, the project file, the MSBuild targets or a publish — the CLI build
///     E2E still owns those.
/// </remarks>
public class TemplatesCompileTests
{
    private static readonly ServerBatteries Everything = new()
    {
        Pwa = true,
        Cqrs = true,
        Data = true,
        Jobs = true,
        Mail = true,
        Cache = true,
        Storage = true,
        Push = true,
        Snapshots = true,
        Logs = true,
        Ops = true,
    };

    [Fact]
    public void A_bare_server_app_compiles()
    {
        var files = ProjectGenerator.GenerateServer("/app", "Shop", new ServerBatteries(), "9.9.9").Files;

        var errors = Errors(files, ServerUsings);

        Assert.True(errors.Count == 0, Describe(errors));
    }

    [Fact]
    public void A_server_app_with_every_battery_compiles()
    {
        var files = ProjectGenerator.GenerateServer("/app", "Shop", Everything, "9.9.9").Files;

        var errors = Errors(files, ServerUsings);

        Assert.True(errors.Count == 0, Describe(errors));
    }

    [Fact]
    public void A_browser_app_compiles()
    {
        var files = ProjectGenerator.GenerateWasm("/app", "Shop", pwa: true, docker: false, "9.9.9").Files;

        var errors = Errors(files, BrowserUsings);

        Assert.True(errors.Count == 0, Describe(errors));
    }

    [Fact]
    public void The_server_half_of_a_hosted_browser_app_compiles()
    {
        var files = ProjectGenerator.GenerateWasmHosted("/app", "Shop", new ServerBatteries(), "9.9.9").Files;

        var errors = Errors(files.Where(f => !InClient(f)), ServerUsings);

        Assert.True(errors.Count == 0, Describe(errors));
    }

    [Fact]
    public void The_browser_half_of_a_hosted_browser_app_compiles()
    {
        var files = ProjectGenerator.GenerateWasmHosted("/app", "Shop", new ServerBatteries(), "9.9.9").Files;

        var errors = Errors(files.Where(InClient), BrowserUsings);

        Assert.True(errors.Count == 0, Describe(errors));
    }

    public static TheoryData<string> FrontEnds() => [.. SpaFramework.All.Select(framework => framework.Key)];

    private static SpaFramework FrontEnd(string key) => SpaFramework.All.Single(framework => framework.Key == key);

    [Theory]
    [MemberData(nameof(FrontEnds))]
    public void The_host_of_a_bare_TypeScript_front_end_compiles(string key)
    {
        var files = ProjectGenerator.GenerateSpa("/app", "Shop", FrontEnd(key), new ServerBatteries(), "9.9.9").Files;

        var errors = Errors(files, ServerUsings);

        Assert.True(errors.Count == 0, Describe(errors));
    }

    [Theory]
    [MemberData(nameof(FrontEnds))]
    public void The_host_of_a_TypeScript_front_end_with_every_battery_compiles(string key)
    {
        var files = ProjectGenerator.GenerateSpa("/app", "Shop", FrontEnd(key), Everything, "9.9.9").Files;

        var errors = Errors(files, ServerUsings);

        Assert.True(errors.Count == 0, Describe(errors));
    }

    [Theory]
    [InlineData("react")]
    [InlineData("vue")]
    [InlineData("lit", "angular", "svelte")]
    public void A_server_app_whose_home_page_renders_its_islands_compiles(params string[] islands)
    {
        var files = ProjectGenerator.GenerateServer("/app", "Shop", new ServerBatteries(), "9.9.9", islands).Files;

        var errors = Errors(files, ServerUsings);

        Assert.True(errors.Count == 0, Describe(errors));
    }

    [Theory]
    [InlineData("lit")]
    [InlineData("preact", "solid")]
    public void A_browser_app_whose_home_page_renders_its_islands_compiles(params string[] islands)
    {
        var files = ProjectGenerator
            .GenerateWasm("/app", "Shop", pwa: true, docker: false, "9.9.9", islands: islands).Files;

        var errors = Errors(files, BrowserUsings);

        Assert.True(errors.Count == 0, Describe(errors));
    }

    [Fact]
    public void A_server_app_whose_home_page_renders_a_Blazor_island_compiles()
    {
        var files = ProjectGenerator.GenerateServer("/app", "Shop", new ServerBatteries(), "9.9.9", ["blazor", "react"]).Files;

        var errors = Errors(files, ServerUsings, RazorClassLibrary(files));

        Assert.True(errors.Count == 0, Describe(errors));
    }

    // The one-project build compiles Client/ into the browser app and leaves it out of the server.
    private static bool InClient(ScaffoldFile file) => file.Path.Contains("/Client/", StringComparison.Ordinal);

    // What a real app is handed without writing it: the SDK's ImplicitUsings, and the usings each
    // package's build/*.props adds. A scaffold's own GlobalUsings.cs carries the rest.
    private const string SharedUsings = """
        global using System;
        global using System.Collections.Generic;
        global using System.IO;
        global using System.Linq;
        global using System.Net.Http;
        global using System.Threading;
        global using System.Threading.Tasks;
        global using Rask.Core;
        global using Virtualize = Rask.Core.Components.Virtualize;
        global using Rask.Web;
        global using Types = Rask.Web.Types;
        global using EditContext = Rask.Core.Forms.EditContext;
        global using FormData = Rask.Core.Live.FormData;
        global using DataTransfer = Rask.Core.DataTransfer;
        global using EventTarget = Rask.Core.EventTarget;
        global using Touch = Rask.Core.Touch;
        global using Rask.Wire;
        global using Rask.Cqrs;

        """;

    private const string ServerUsings = SharedUsings + """
        global using System.Net.Http.Json;
        global using Microsoft.AspNetCore.Builder;
        global using Microsoft.AspNetCore.Hosting;
        global using Microsoft.AspNetCore.Http;
        global using Microsoft.AspNetCore.Routing;
        global using Microsoft.Extensions.Configuration;
        global using Microsoft.Extensions.DependencyInjection;
        global using Microsoft.Extensions.Hosting;
        global using Microsoft.Extensions.Logging;
        global using Rask.Auth;
        global using Rask.Data;
        global using Rask.Logging;
        global using Rask.Mailing;
        global using Rask.Caching;
        global using Rask.Outbox;
        global using Rask.Background;
        global using Rask.Storage;
        global using Rask.Api;
        """;

    private const string BrowserUsings = SharedUsings + """
        global using Rask.Auth.Client;
        """;

    /// <summary>
    ///     The Razor class library a Blazor island hosts its component from, as the reference the app sees.
    /// </summary>
    /// <remarks>
    ///     No Razor compiler runs here, so the component is its own <c>@code</c> block in a class — which is
    ///     everything the island reads off it: the <c>[Parameter]</c>s its chain steps are generated from.
    /// </remarks>
    private static MetadataReference RazorClassLibrary(IEnumerable<ScaffoldFile> files)
    {
        var razor = files.Single(f => f.Path.EndsWith("BlazorCounter.razor", StringComparison.Ordinal)).Content;
        var members = razor[(razor.IndexOf("@code {", StringComparison.Ordinal) + "@code {".Length)..razor.LastIndexOf('}')];
        var source = $$"""
            using Microsoft.AspNetCore.Components;
            namespace Shop.Components;
            public class BlazorCounter : ComponentBase { {{members}} }
            """;

        return CSharpCompilation.Create(
            "Shop.Components",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            GeneratorDriverFixture.BuildReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)).ToMetadataReference();
    }

    private static List<Diagnostic> Errors(
        IEnumerable<ScaffoldFile> files, string usings, MetadataReference? razorClassLibrary = null)
    {
        var parse = new CSharpParseOptions(LanguageVersion.Latest);

        // The app's own sources: not its test project, and not the Razor class library beside it.
        var trees = files
            .Where(f => f.Path.EndsWith(".cs", StringComparison.Ordinal)
                        && !f.Path.Contains(".Tests/", StringComparison.Ordinal)
                        && !f.Path.Contains(".Components/", StringComparison.Ordinal))
            .Select(f => CSharpSyntaxTree.ParseText(f.Content, parse, f.Path))
            .Append(CSharpSyntaxTree.ParseText(usings, parse, "ImplicitUsings.g.cs"))
            .ToArray();

        var compilation = CSharpCompilation.Create(
            "Shop",
            trees,
            razorClassLibrary is null
                ? GeneratorDriverFixture.BuildReferences()
                : [.. GeneratorDriverFixture.BuildReferences(), GeneratorDriverFixture.AssemblyReference("Rask.Blazor"), razorClassLibrary],
            new CSharpCompilationOptions(OutputKind.ConsoleApplication, nullableContextOptions: NullableContextOptions.Enable));

        CSharpGeneratorDriver
            .Create(
                new ComponentFactoryGenerator().AsSourceGenerator(),
                new RoutesGenerator().AsSourceGenerator(),
                new ValidatorRegistryGenerator().AsSourceGenerator(),
                new ReadModelGenerator().AsSourceGenerator(),
                new ModelRegistryGenerator().AsSourceGenerator(),
                new ModelInputGenerator().AsSourceGenerator(),
                new AuthUserGenerator().AsSourceGenerator(),
                new CqrsDispatchGenerator().AsSourceGenerator(),
                new CqrsCodecGenerator().AsSourceGenerator(),
                new ApiClientGenerator().AsSourceGenerator(),
                new ExternalGenerator().AsSourceGenerator(),
                new BlazorGenerator().AsSourceGenerator())
            .WithUpdatedParseOptions(parse)
            .WithUpdatedAnalyzerConfigOptions(new AppOptions())
            .RunGeneratorsAndUpdateCompilation(compilation, out var generated, out _);

        return generated.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
    }

    private static string Describe(List<Diagnostic> errors) =>
        $"{errors.Count} error(s) in the scaffold:\n" + string.Join('\n', errors.Take(25).Select(e => e.ToString()));

    private sealed class AppOptions : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Properties();

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;

        private sealed class Properties : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
            {
                value = key == "build_property.RootNamespace" ? "Shop" : null;
                return value is not null;
            }
        }
    }
}
