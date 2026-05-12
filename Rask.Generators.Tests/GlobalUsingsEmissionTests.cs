using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Rask.Generators.Tests;

public class GlobalUsingsEmissionTests
{
    [Fact]
    public void OneUserNamespace_EmitsTagsAndComponentsImports()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed class Widget : Component
                  {
                      public override Component Render() => this;
                  }
                  """;

        var output = Run(src);

        Assert.Contains("global using static global::Rask.Core.Tags;", output);
        Assert.Contains("global using static global::Demo.Components;", output);
    }

    [Fact]
    public void MultipleUserNamespaces_OneLinePerNamespace()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo.Pages
                  {
                      public sealed class Widget : Component { public override Component Render() => this; }
                  }
                  namespace Demo.Other
                  {
                      public sealed class Gadget : Component { public override Component Render() => this; }
                  }
                  """;

        var output = Run(src);

        Assert.Contains("global using static global::Rask.Core.Tags;", output);
        Assert.Contains("global using static global::Demo.Pages.Components;", output);
        Assert.Contains("global using static global::Demo.Other.Components;", output);
    }

    [Fact]
    public void NoUserComponents_StillEmitsTagsImport()
    {
        var src = """
                  namespace Demo;
                  public class NotAComponent { }
                  """;

        var output = Run(src);

        Assert.Contains("global using static global::Rask.Core.Tags;", output);
        Assert.DoesNotContain("global using static global::Demo.Components;", output);
    }

    [Fact]
    public void OptOutProperty_SkipsEmission()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed class Widget : Component
                  {
                      public override Component Render() => this;
                  }
                  """;

        var generated = RunRaw(src, new Dictionary<string, string> { ["build_property.RaskGlobalUsings"] = "false" });

        Assert.DoesNotContain(generated, s => s.HintName.Contains("RaskGlobalUsings.g.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void DeeplyNestedNamespace_EmitsFullyQualifiedImport()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo.App.Pages.Account;
                  public sealed class Widget : Component
                  {
                      public override Component Render() => this;
                  }
                  """;

        var output = Run(src);

        Assert.Contains("global using static global::Demo.App.Pages.Account.Components;", output);
    }

    [Fact]
    public void ComponentInGlobalNamespace_NoExtraImport()
    {
        // A user component in the global namespace has no namespace prefix to attach,
        // and `Components` (a free static class) cannot reach it via `using static`.
        // The Tags line must still emit; no extra `using static .Components;` line.
        var src = """
                  using Rask.Core;
                  public sealed class Widget : Component
                  {
                      public override Component Render() => this;
                  }
                  """;

        var output = Run(src);

        Assert.Contains("global using static global::Rask.Core.Tags;", output);
        Assert.DoesNotContain("global using static global::.Components;", output);
        Assert.DoesNotContain("global using static global::Components;", output);
    }

    [Fact]
    public void InternalUserComponent_IncludedInImport()
    {
        // Internal components still emit a {Ns}.Components factory; the global
        // using must cover them so users can call them from the same assembly
        // without a manual import.
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  internal sealed class Widget : Component
                  {
                      public override Component Render() => this;
                  }
                  """;

        var output = Run(src);

        Assert.Contains("global using static global::Demo.Components;", output);
    }

    private static string Run(string source) => Run(source, null);

    private static string Run(string source, Dictionary<string, string>? buildProps)
    {
        var generated = RunRaw(source, buildProps);
        var match = generated.FirstOrDefault(s =>
            s.HintName.Contains("RaskGlobalUsings.g.cs", StringComparison.Ordinal));
        if (match.SourceText is null)
        {
            var available = string.Join(", ", generated.Select(s => s.HintName));
            throw new InvalidOperationException(
                $"No RaskGlobalUsings.g.cs generated. Available: [{available}]");
        }

        return match.SourceText.ToString();
    }

    private static ImmutableArray<GeneratedSourceResult> RunRaw(string source, Dictionary<string, string>? buildProps)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var references = BuildReferences();
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var driver = CSharpGeneratorDriver
            .Create(new ComponentFactoryGenerator())
            .WithUpdatedParseOptions(new CSharpParseOptions(LanguageVersion.Latest));

        if (buildProps is { Count: > 0 })
        {
            driver = (CSharpGeneratorDriver)driver.WithUpdatedAnalyzerConfigOptions(
                new TestAnalyzerConfigOptionsProvider(buildProps));
        }

        var result = driver.RunGenerators(compilation).GetRunResult();
        return result.Results.SelectMany(r => r.GeneratedSources).ToImmutableArray();
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
