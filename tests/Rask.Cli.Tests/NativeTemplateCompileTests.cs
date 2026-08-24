using System.Collections.Immutable;
using System.Reflection;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Rask.Cli.Scaffolding;
using Rask.Generators;

namespace Rask.Cli.Tests;

/// <summary>
///     Compiles the C# that <c>rask new --template native</c> writes, against the in-repo Rask assemblies
///     and the real generator.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="CliBuildE2E" /> packs this commit's packages and runs a real <c>dotnet build</c> over
///         what the CLI writes, and it covers the server, wasm and wasm-hosted templates. It cannot cover
///         the <b>native</b> one: building that needs the iOS and Android workloads, which the Ubuntu
///         runner does not have. The templates are raw strings, so no in-repo build compiles them either —
///         which left the native template verified by nothing at all.
///     </para>
///     <para>
///         That gap was not theoretical. Dropping the factory (#792) turned every factory call in a
///         template into <c>CS1955</c>. The packaged gate caught the one in the <c>--auth</c> LoginPage
///         exactly as designed and could not see the one in the native App shell, so
///         <c>rask new --template native</c> would have handed somebody a project that does not compile —
///         and the native template is the one a beginner is least able to debug (#795).
///     </para>
///     <para>
///         So this gate takes the cheaper half of the job and runs it <b>always</b>, with no workloads, no
///         packing and no network: a Roslyn compilation over the template's own component code with
///         <see cref="ComponentFactoryGenerator" /> and <see cref="RoutesGenerator" /> running, which is
///         exactly the markup-surface class of break that actually happened. The platform heads under
///         <c>Platforms/</c> are left to the workload-bearing build — they are UIKit and Android glue, not
///         markup, and nothing in the chain's surface can break them.
///     </para>
/// </remarks>
public sealed class NativeTemplateCompileTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void The_native_templates_component_code_compiles(bool ios, bool android)
    {
        var diagnostics = CompileTemplateComponents(ios, android);

        Assert.True(
            diagnostics.Length == 0,
            "rask new --template native emitted component code that does not compile:\n"
            + string.Join("\n", diagnostics.Select(d => d.ToString())));
    }

    /// <summary>
    ///     The negative control. A gate that compiles nothing reports no errors, which is indistinguishable
    ///     from a gate that compiles everything — so this proves the harness actually binds the chain and
    ///     would fail on the break it exists to catch.
    /// </summary>
    [Fact]
    public void The_harness_reports_the_factory_call_the_shipped_break_was()
    {
        // `Html(...)` is the factory spelling #792 removed: the chain entry is a property, so calling it
        // is CS1955 "Non-invocable member cannot be used like a method" — the exact diagnostic that
        // reached the native template unseen.
        var diagnostics = Compile([("App.cs", """
            namespace Probe;

            public sealed partial class Probe : global::Rask.Core.Component
            {
                protected override global::Rask.Core.Component? Render() => Html("en");
            }
            """)]);

        Assert.Contains(diagnostics, d => d.Id == "CS1955");
    }

    private static ImmutableArray<Diagnostic> CompileTemplateComponents(bool ios, bool android)
    {
        var directory = Path.Combine(Path.GetTempPath(), "rask-native-template-" + Guid.NewGuid().ToString("N"));
        var result = ProjectGenerator.GenerateNative(directory, "NativeProbe", "native", "1.0.0", ios, android);

        // The component code only. Platforms/ is UIKit and Android glue that needs the workloads, and the
        // csproj / manifests are not C# at all.
        var sources = result.Files
            .Where(f => f.Path.EndsWith(".cs", StringComparison.Ordinal)
                && f.Path.Contains(Path.DirectorySeparatorChar + "Features" + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal))
            .Select(f => (Path.GetFileName(f.Path), f.Content))
            .ToArray();

        // If the template stops writing component code, this gate would pass by compiling nothing.
        Assert.NotEmpty(sources);

        return Compile(sources);
    }

    private static ImmutableArray<Diagnostic> Compile((string Name, string Source)[] sources)
    {
        var trees = sources
            .Append((Name: "GlobalUsings.cs", Source: GlobalUsings()))
            .Select(s => CSharpSyntaxTree.ParseText(
                s.Source, new CSharpParseOptions(LanguageVersion.Latest), s.Name))
            .ToArray();

        var compilation = CSharpCompilation.Create(
            "NativeProbe",
            trees,
            References(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        CSharpGeneratorDriver
            .Create(new ComponentFactoryGenerator(), new RoutesGenerator())
            .WithUpdatedParseOptions(new CSharpParseOptions(LanguageVersion.Latest))
            .RunGeneratorsAndUpdateCompilation(compilation, out var generated, out _);

        return generated.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();
    }

    /// <summary>
    ///     The global usings a scaffolded native project actually gets, read from the props
    ///     <c>Rask.Native</c> ships rather than restated here.
    /// </summary>
    /// <remarks>
    ///     These are load-bearing, not cosmetic: without <c>Rask.Core</c> in scope the template's classes
    ///     do not resolve <c>Component</c>, so they are not components, so the generator injects no chain
    ///     entries and every <c>Div</c> / <c>H1</c> in the page becomes CS0103. A hard-coded copy would
    ///     drift silently and start failing this gate for a reason that has nothing to do with the
    ///     template, so the props file is the source of truth — the same file the package installs.
    /// </remarks>
    private static string GlobalUsings()
    {
        var props = XDocument.Load(
            Path.Combine(CliBuildE2E.FindRepoRoot(), "src", "Rask.Native", "build", "Rask.Native.props"));

        var usings = props.Descendants("Using")
            .Select(e => (Namespace: (string?)e.Attribute("Include"), Alias: (string?)e.Attribute("Alias")))
            .Where(u => u.Namespace is { Length: > 0 })
            .Select(u => u.Alias is { Length: > 0 } alias
                ? $"global using {alias} = {u.Namespace};"
                : $"global using {u.Namespace};")
            .ToList();

        Assert.NotEmpty(usings);

        // What <ImplicitUsings>enable</ImplicitUsings> adds on top, which the generated csproj sets.
        usings.AddRange([
            "global using System;",
            "global using System.Collections.Generic;",
            "global using System.IO;",
            "global using System.Linq;",
            "global using System.Net.Http;",
            "global using System.Threading;",
            "global using System.Threading.Tasks;",
        ]);

        return string.Join("\n", usings);
    }

    // The runtime's own reference set plus every Rask assembly a native project references. Loading them
    // by name pins the gate to THIS commit's assemblies, which is the whole point — a template has to
    // compile against the code it ships beside.
    private static ImmutableArray<MetadataReference> References()
    {
        var refs = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();

        foreach (var name in new[] { "Rask.Core", "Rask.Html", "Rask.Chrome", "Rask.Native" })
        {
            refs.Add(MetadataReference.CreateFromFile(Assembly.Load(name).Location));
        }

        return [.. refs];
    }
}
