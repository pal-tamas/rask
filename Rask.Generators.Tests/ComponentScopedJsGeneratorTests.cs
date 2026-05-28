using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Rask.Generators.Tests;

public class ComponentScopedJsGeneratorTests
{
    [Fact]
    public void Generator_EmitsRegistrationForMatchingComponentAndJsSibling()
    {
        const string source = """
                              namespace Foo;
                              public sealed class Counter : Rask.Core.Component
                              {
                                  protected override Rask.Core.RenderResult Render() => this;
                              }
                              """;

        var run = Run(
            new[] { ("/proj/Counter.cs", source) },
            new[] { ("/proj/Counter.js", "export function rendered(el) { el.dataset.rendered = '1'; }") });

        var generated = run.GeneratedSource("__RaskScopedJsRegistration");
        Assert.Contains("typeof(global::Foo.Counter)", generated);
        Assert.Contains("el.dataset.rendered", generated);
        Assert.Contains("RegisterJs", generated);
        Assert.Contains("global::Rask.Core.ScopedAssets.ScopedAssetRegistry.RegisterJs", generated);
        Assert.Contains("ModuleInitializer", generated);
        Assert.DoesNotContain(run.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generator_DoesNotPairJsWithComponentInDifferentDirectory()
    {
        const string source = """
                              namespace Foo;
                              public sealed class Counter : Rask.Core.Component
                              {
                                  protected override Rask.Core.RenderResult Render() => this;
                              }
                              """;

        var run = Run(
            new[] { ("/proj/Other/Counter.cs", source) },
            new[] { ("/proj/Pages/Counter.js", "export function rendered(el) {}") });

        Assert.Contains(run.Diagnostics, d => d.Id == "RASK017");
    }

    [Fact]
    public void Generator_RaisesRASK017_ForOrphanJsFile()
    {
        const string source = """
                              namespace Foo;
                              public sealed class Counter : Rask.Core.Component
                              {
                                  protected override Rask.Core.RenderResult Render() => this;
                              }
                              """;

        var run = Run(
            new[] { ("/proj/Counter.cs", source) },
            new[] { ("/proj/Conter.js", "export function rendered(el) {}") });

        Assert.Contains(run.Diagnostics, d => d.Id == "RASK017");
    }

    [Fact]
    public void Generator_EscapesQuotesInJsContent()
    {
        const string source = """
                              namespace Foo;
                              public sealed class Counter : Rask.Core.Component
                              {
                                  protected override Rask.Core.RenderResult Render() => this;
                              }
                              """;

        var run = Run(
            new[] { ("/proj/Counter.cs", source) },
            new[] { ("/proj/Counter.js", "export function rendered(el) { el.textContent = \"hi\"; }") });

        var generated = run.GeneratedSource("__RaskScopedJsRegistration");
        // verbatim string literal escapes " as ""
        Assert.Contains("\"\"hi\"\"", generated);
        Assert.DoesNotContain(run.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generator_IgnoresWhitespaceOnlyJsFile()
    {
        const string source = """
                              namespace Foo;
                              public sealed class Counter : Rask.Core.Component
                              {
                                  protected override Rask.Core.RenderResult Render() => this;
                              }
                              """;

        var run = Run(
            new[] { ("/proj/Counter.cs", source) },
            new[] { ("/proj/Counter.js", "   \n\t  ") });

        Assert.DoesNotContain(run.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var anyRegistration = run.RunResult.Results
            .SelectMany(r => r.GeneratedSources)
            .Any(s => s.HintName.Contains("__RaskScopedJsRegistration"));
        Assert.False(anyRegistration);
    }

    [Fact]
    public void Generator_SkipsAbstractComponents()
    {
        const string source = """
                              namespace Foo;
                              public abstract class Base : Rask.Core.Component
                              {
                                  protected override Rask.Core.RenderResult Render() => this;
                              }
                              """;

        var run = Run(
            new[] { ("/proj/Base.cs", source) },
            new[] { ("/proj/Base.js", "export function rendered(el) {}") });

        Assert.Contains(run.Diagnostics, d => d.Id == "RASK017");
    }

    private static GeneratorRun Run(
        (string Path, string Source)[] sources,
        (string Path, string Contents)[] additionalJs)
    {
        var trees = sources
            .Select(s => CSharpSyntaxTree.ParseText(
                s.Source,
                new CSharpParseOptions(LanguageVersion.Latest),
                s.Path))
            .ToArray();

        var references = BuildReferences();
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var additionalTexts = additionalJs
            .Select(t => (AdditionalText)new InMemoryAdditionalText(t.Path, t.Contents))
            .ToImmutableArray();

        var driver = CSharpGeneratorDriver
            .Create(new ComponentScopedJsGenerator())
            .WithUpdatedParseOptions(new CSharpParseOptions(LanguageVersion.Latest))
            .AddAdditionalTexts(additionalTexts);

        var runResult = driver.RunGenerators(compilation).GetRunResult();
        return new GeneratorRun(runResult, compilation);
    }

    private static ImmutableArray<MetadataReference> BuildReferences()
    {
        var trustedAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        var refs = trustedAssemblies
            .Select(path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()
            .ToList();

        var raskCore = Assembly.Load("Rask.Core");
        refs.Add(MetadataReference.CreateFromFile(raskCore.Location));
        return refs.ToImmutableArray();
    }

    private sealed class InMemoryAdditionalText : AdditionalText
    {
        private readonly string _contents;

        public InMemoryAdditionalText(string path, string contents)
        {
            Path = path;
            _contents = contents;
        }

        public override string Path { get; }

        public override SourceText? GetText(CancellationToken cancellationToken = default)
            => SourceText.From(_contents);
    }
}
