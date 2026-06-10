using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Rask.Generators.Tests;

public class FactoryNavigationEmissionTests
{
    private const string Src = """
                               using Rask.Core;
                               namespace Demo;
                               public sealed class Widget : Component
                               {
                                   public string Name { get; set; }
                                   public override RenderResult Render() => this;
                               }
                               """;

    [Fact]
    public void DefaultOn_EmitsDebuggerStepThroughAndSeeCrefBreadcrumb()
    {
        var run = GeneratorDriverFixture.Run(Src);
        var output = run.GeneratedSource("Demo.Generated.g.cs");

        // Debugger skips the factory into user code.
        Assert.Contains("[global::System.Diagnostics.DebuggerStepThrough]", output);
        // Quick-Doc / hover breadcrumb links to the component type.
        Assert.Contains("<see cref=\"global::Demo.Widget\"/>", output);
        // No #line: it does nothing for navigation in stock Roslyn/ReSharper.
        Assert.DoesNotContain("#line", output);
        // Signature unchanged.
        Assert.Contains("Widget(string Name, object? Key = null)", output);
    }

    [Fact]
    public void GenericComponent_CrefUsesBraceArity()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed class Box<T> : Component where T : class
                  {
                      public override RenderResult Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.Run(src);
        var output = run.GeneratedSource("Demo.Generated.g.cs");

        // Generic arity uses `{T}` (doc-comment cref syntax), not `<T>`, so the cref resolves.
        Assert.Contains("<see cref=\"global::Demo.Box{T}\"/>", output);
    }

    [Fact]
    public void OptOut_DropsBreadcrumbButKeepsDebuggerStepThrough()
    {
        var output = Run(Src,
            new Dictionary<string, string> { ["build_property.RaskFactoryNavigation"] = "false" });

        Assert.DoesNotContain("<see cref", output);
        Assert.Contains("[global::System.Diagnostics.DebuggerStepThrough]", output);
    }

    private static string Run(string source, Dictionary<string, string> buildProps)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            GeneratorDriverFixture.BuildReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var driver = (CSharpGeneratorDriver)CSharpGeneratorDriver
            .Create(new ComponentFactoryGenerator())
            .WithUpdatedParseOptions(new CSharpParseOptions(LanguageVersion.Latest))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(buildProps));

        var generated = driver.RunGenerators(compilation).GetRunResult()
            .Results.SelectMany(r => r.GeneratedSources)
            .ToImmutableArray();
        var match = generated.FirstOrDefault(s =>
            s.HintName.Contains("Demo.Generated.g.cs", StringComparison.Ordinal));
        if (match.SourceText is null)
        {
            var available = string.Join(", ", generated.Select(s => s.HintName));
            throw new InvalidOperationException(
                $"No Demo.Generated.g.cs generated. Available: [{available}]");
        }

        return match.SourceText.ToString();
    }

    private sealed class TestAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
    {
        public TestAnalyzerConfigOptionsProvider(Dictionary<string, string> values) =>
            GlobalOptions = new DictOptions(values);

        public override AnalyzerConfigOptions GlobalOptions { get; }

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;

        private sealed class DictOptions : AnalyzerConfigOptions
        {
            private readonly Dictionary<string, string> _values;

            public DictOptions(Dictionary<string, string> values) => _values = values;

            public override bool TryGetValue(string key, out string value)
            {
                if (_values.TryGetValue(key, out var v))
                {
                    value = v;
                    return true;
                }

                value = string.Empty;
                return false;
            }
        }
    }
}
