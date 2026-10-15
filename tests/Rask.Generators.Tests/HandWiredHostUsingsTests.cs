using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Rask.Generators.Tests;

/// <summary>
///     A plain <c>Microsoft.NET.Sdk.Web</c> project that references Rask.Server and wires the host by hand (#1236):
///     compiled here under exactly the global usings it would get — the Web SDK's implicit set plus every
///     <c>&lt;Using&gt;</c> the build files of Rask.Server and the packages it brings declare.
/// </summary>
/// <remarks>
///     Compiled rather than asserted on the targets' text, because the failure was a COMPILE error far from its
///     cause: <c>AddRask</c> not found (CS1061), and <c>[Route]</c> resolving to ASP.NET's <c>Route</c> class
///     (CS0616). The second half pins the reason <c>Rask.Core.Routing</c> stays out of the project-wide set.
/// </remarks>
public sealed class HandWiredHostUsingsTests
{
    // The Web SDK's implicit usings (Microsoft.NET.Sdk.Web.ProjectSystem.targets) for ImplicitUsings=enable.
    private static readonly string[] _webSdkUsings =
    [
        "System", "System.Collections.Generic", "System.IO", "System.Linq", "System.Net.Http",
        "System.Net.Http.Json", "System.Threading", "System.Threading.Tasks",
        "Microsoft.AspNetCore.Builder", "Microsoft.AspNetCore.Hosting", "Microsoft.AspNetCore.Http",
        "Microsoft.AspNetCore.Routing", "Microsoft.Extensions.Configuration",
        "Microsoft.Extensions.DependencyInjection", "Microsoft.Extensions.Hosting", "Microsoft.Extensions.Logging",
    ];

    private const string Program = """
        public static class Host
        {
            public static void Start(string[] args)
            {
                var builder = WebApplication.CreateBuilder(args);
                builder.Services.AddRask();

                var app = builder.Build();
                app.UseWebSockets();
                app.MapRask<App>(pathBase: "/new");
                app.Run();
            }
        }

        public sealed class App : Component
        {
            protected override Component? Render() => null;
        }
        """;

    [Fact]
    public void A_hand_wired_web_host_finds_AddRask_and_MapRask_with_no_using_of_its_own()
    {
        var compilation = Compile(Program);

        var errors = Errors(compilation);

        Assert.Empty(errors);
    }

    [Fact]
    public void A_page_file_that_imports_the_routing_namespace_compiles_its_Route_attribute()
    {
        const string page = """
            using Rask.Core.Routing;

            [Route("/")]
            public sealed class HomePage : Component
            {
                protected override Component? Render() => null;
            }
            """;

        var compilation = Compile(Program, page);

        var errors = Errors(compilation);

        Assert.Empty(errors);
    }

    [Fact]
    public void An_existing_controller_s_Route_still_means_MVC_s_once_Rask_Server_arrives()
    {
        // The app this is added to already has controllers. Importing Rask.Core.Routing for the whole project
        // would make this [Route] ambiguous between Rask's and MVC's (CS0104) — the reason it is not imported.
        const string controller = """
            using Microsoft.AspNetCore.Mvc;

            [ApiController]
            [Route("api/orders")]
            public sealed class OrdersController : ControllerBase
            {
            }
            """;

        var compilation = Compile(Program, controller);

        var errors = Errors(compilation);
        var route = compilation.GetTypeByMetadataName("OrdersController")!.GetAttributes()
            .Single(a => a.AttributeClass!.Name == "RouteAttribute");
        Assert.Empty(errors);
        Assert.Equal("Microsoft.AspNetCore.Mvc.RouteAttribute", route.AttributeClass!.ToDisplayString());
    }

    private static List<Diagnostic> Errors(CSharpCompilation compilation) =>
        compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

    private static CSharpCompilation Compile(params string[] sources)
    {
        var parse = new CSharpParseOptions(LanguageVersion.Latest);
        var trees = sources
            .Select((s, i) => CSharpSyntaxTree.ParseText(s, parse, path: $"File{i}.cs"))
            .Append(CSharpSyntaxTree.ParseText(GlobalUsings(), parse, path: "GlobalUsings.g.cs"));

        return CSharpCompilation.Create(
            "HandWiredHost",
            trees,
            References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }

    private static readonly Lazy<MetadataReference[]> References = new(() =>
        ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToArray());

    // What the SDK's GenerateGlobalUsings writes for the project: one line per <Using> item.
    private static string GlobalUsings()
    {
        var lines = _webSdkUsings.Select(ns => $"global using global::{ns};").ToList();
        foreach (var item in PackageUsings())
        {
            var include = item.Attribute("Include")!.Value;
            lines.Add((string?)item.Attribute("Alias") is { } alias
                ? $"global using {alias} = global::{include};"
                : $"global using global::{include};");
        }

        return string.Join('\n', lines.Distinct(StringComparer.Ordinal));
    }

    // Every <Using> declared at evaluation time (not inside a target) by the build files of Rask.Server and every
    // project it references, transitively — the packages a consumer of Rask.Server gets build files from.
    private static IEnumerable<XElement> PackageUsings()
    {
        var src = Path.Combine(RepoRoot(), "src");
        foreach (var project in ReferencedProjects(Path.Combine(src, "Rask.Server", "Rask.Server.csproj")))
        {
            var dir = Path.GetDirectoryName(project)!;
            var buildDir = Directory.Exists(Path.Combine(dir, "buildTransitive"))
                ? Path.Combine(dir, "buildTransitive")
                : Path.Combine(dir, "build");
            if (!Directory.Exists(buildDir))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(buildDir).Where(f => f.EndsWith(".props", StringComparison.Ordinal) || f.EndsWith(".targets", StringComparison.Ordinal)))
            {
                foreach (var item in XDocument.Load(file).Root!.Elements("ItemGroup").Elements("Using"))
                {
                    yield return item;
                }
            }
        }
    }

    private static HashSet<string> ReferencedProjects(string root)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>([Path.GetFullPath(root)]);
        while (pending.Count > 0)
        {
            var project = pending.Pop();
            if (!seen.Add(project))
            {
                continue;
            }

            foreach (var reference in XDocument.Load(project).Descendants("ProjectReference"))
            {
                // An analyzer/generator reference ships no build files of its own to the consumer.
                if ((string?)reference.Attribute("OutputItemType") == "Analyzer")
                {
                    continue;
                }

                var include = reference.Attribute("Include")!.Value.Replace('\\', Path.DirectorySeparatorChar);
                pending.Push(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!, include)));
            }
        }

        return seen;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Rask.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
