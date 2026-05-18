using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Rask.Generators.Tests;

public class ComponentScopedCssGeneratorTests
{
    [Fact]
    public void Generator_EmitsRegistrationForMatchingComponentAndCssSibling()
    {
        const string source = """
            namespace Foo;
            public sealed class Counter : Rask.Core.Component
            {
                protected override Rask.Core.Component Render() => this;
            }
            """;

        var run = Run(
            new[] { ("/proj/Counter.cs", source) },
            new[] { ("/proj/Counter.css", ".counter { color: red; }") });

        var generated = run.GeneratedSource("__RaskScopedCssRegistration");
        Assert.Contains("typeof(global::Foo.Counter)", generated);
        Assert.Contains(".counter { color: red; }", generated);
        Assert.Contains("RegisterType", generated);
        Assert.Contains("ModuleInitializer", generated);
        Assert.DoesNotContain(run.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generator_DoesNotPairCssWithComponentInDifferentDirectory()
    {
        // Counter.css under /proj/Pages/ should not bind to /proj/Other/Counter (same simple
        // name, different directory) — they're orphan from each other's perspective.
        const string source = """
            namespace Foo;
            public sealed class Counter : Rask.Core.Component
            {
                protected override Rask.Core.Component Render() => this;
            }
            """;

        var run = Run(
            new[] { ("/proj/Other/Counter.cs", source) },
            new[] { ("/proj/Pages/Counter.css", ".counter { color: red; }") });

        Assert.Contains(run.Diagnostics, d => d.Id == "RASK015");
    }

    [Fact]
    public void Generator_RaisesRASK015_ForOrphanCssFile()
    {
        const string source = """
            namespace Foo;
            public sealed class Counter : Rask.Core.Component
            {
                protected override Rask.Core.Component Render() => this;
            }
            """;

        var run = Run(
            new[] { ("/proj/Counter.cs", source) },
            new[] { ("/proj/Conter.css", ".typo { color: red; }") });

        Assert.Contains(run.Diagnostics, d => d.Id == "RASK015");
    }

    [Fact]
    public void Generator_EscapesQuotesInCssContent()
    {
        const string source = """
            namespace Foo;
            public sealed class Counter : Rask.Core.Component
            {
                protected override Rask.Core.Component Render() => this;
            }
            """;

        var run = Run(
            new[] { ("/proj/Counter.cs", source) },
            new[] { ("/proj/Counter.css", ".x::before { content: \"a\"; }") });

        var generated = run.GeneratedSource("__RaskScopedCssRegistration");
        // verbatim string literal escapes " as ""
        Assert.Contains("\"\"a\"\"", generated);
        Assert.DoesNotContain(run.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generator_IgnoresWhitespaceOnlyCssFile()
    {
        const string source = """
            namespace Foo;
            public sealed class Counter : Rask.Core.Component
            {
                protected override Rask.Core.Component Render() => this;
            }
            """;

        var run = Run(
            new[] { ("/proj/Counter.cs", source) },
            new[] { ("/proj/Counter.css", "   \n\t  ") });

        // Whitespace-only CSS is silently ignored — neither a registration nor an orphan
        // diagnostic. The file exists but contributes nothing.
        Assert.DoesNotContain(run.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var anyRegistration = run.RunResult.Results
            .SelectMany(r => r.GeneratedSources)
            .Any(s => s.HintName.Contains("__RaskScopedCssRegistration"));
        Assert.False(anyRegistration);
    }

    [Fact]
    public void Generator_SkipsAbstractComponents()
    {
        // An abstract Component in the same directory as a .css file shouldn't match —
        // abstract classes can't be instantiated and shouldn't host scoped styles. The
        // pairing rule excludes them; the .css file becomes an orphan (RASK015).
        const string source = """
            namespace Foo;
            public abstract class Base : Rask.Core.Component
            {
                protected override Rask.Core.Component Render() => this;
            }
            """;

        var run = Run(
            new[] { ("/proj/Base.cs", source) },
            new[] { ("/proj/Base.css", ".x { color: red; }") });

        Assert.Contains(run.Diagnostics, d => d.Id == "RASK015");
    }

    private static GeneratorRun Run(
        (string Path, string Source)[] sources,
        (string Path, string Contents)[] additionalCss)
    {
        var trees = sources
            .Select(s => CSharpSyntaxTree.ParseText(
                s.Source,
                new CSharpParseOptions(LanguageVersion.Latest),
                path: s.Path))
            .ToArray();

        var references = BuildReferences();
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var additionalTexts = additionalCss
            .Select(t => (AdditionalText)new InMemoryAdditionalText(t.Path, t.Contents))
            .ToImmutableArray();

        var driver = CSharpGeneratorDriver
            .Create(new ComponentScopedCssGenerator())
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
