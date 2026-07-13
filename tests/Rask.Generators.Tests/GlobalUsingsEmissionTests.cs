using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Rask.Generators.Tests;

public class GlobalUsingsEmissionTests
{
    [Fact]
    public void OneUserNamespace_EmitsFrameworkAndUserImports()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed class Widget : Component
                  {
                      public override Component? Render() => this;
                  }
                  """;

        var output = Run(src);

        Assert.DoesNotContain("global using static global::Rask.Core.Tags;", output);
        Assert.Contains("global using static global::Rask.Core.Components.Generated;", output);
        Assert.Contains("global using static global::Rask.Core.Routing.Generated;", output);
        Assert.Contains("global using static global::Demo.Generated;", output);
    }

    [Fact]
    public void MultipleUserNamespaces_OneLinePerNamespace()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo.Pages
                  {
                      public sealed class Widget : Component { public override Component? Render() => this; }
                  }
                  namespace Demo.Other
                  {
                      public sealed class Gadget : Component { public override Component? Render() => this; }
                  }
                  """;

        var output = Run(src);

        Assert.DoesNotContain("global using static global::Rask.Core.Tags;", output);
        Assert.Contains("global using static global::Demo.Pages.Generated;", output);
        Assert.Contains("global using static global::Demo.Other.Generated;", output);
    }

    [Fact]
    public void NoUserComponents_StillEmitsFrameworkImports()
    {
        var src = """
                  namespace Demo;
                  public class NotAComponent { }
                  """;

        var output = Run(src);

        Assert.DoesNotContain("global using static global::Rask.Core.Tags;", output);
        Assert.Contains("global using static global::Rask.Core.Components.Generated;", output);
        Assert.Contains("global using static global::Rask.Core.Routing.Generated;", output);
        Assert.DoesNotContain("global using static global::Demo.Generated;", output);
    }

    [Fact]
    public void OptOutProperty_SkipsEmission()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed class Widget : Component
                  {
                      public override Component? Render() => this;
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
                      public override Component? Render() => this;
                  }
                  """;

        var output = Run(src);

        Assert.Contains("global using static global::Demo.App.Pages.Account.Generated;", output);
    }

    [Fact]
    public void ComponentInGlobalNamespace_NoExtraImport()
    {
        // A user component in the global namespace has no namespace prefix to attach,
        // and `Components` (a free static class) cannot reach it via `using static`.
        // The framework imports must still emit; no extra `using static .Generated;` line.
        var src = """
                  using Rask.Core;
                  public sealed class Widget : Component
                  {
                      public override Component? Render() => this;
                  }
                  """;

        var output = Run(src);

        Assert.Contains("global using static global::Rask.Core.Components.Generated;", output);
        Assert.DoesNotContain("global using static global::.Generated;", output);
        Assert.DoesNotContain("global using static global::Components;", output);
    }

    [Fact]
    public void InternalUserComponent_IncludedInImport()
    {
        // Internal components still emit a {Ns}.Generated factory; the global
        // using must cover them so users can call them from the same assembly
        // without a manual import.
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  internal sealed class Widget : Component
                  {
                      public override Component? Render() => this;
                  }
                  """;

        var output = Run(src);

        Assert.Contains("global using static global::Demo.Generated;", output);
    }

    [Fact]
    public void ReferencedAssemblyWithFactoryMarker_EmitsFactoryGlobalUsing()
    {
        // BuildReferences() includes Rask.Native, which carries
        // [assembly: RaskFactoryNamespace("Rask.Native.Components")] — so a consumer that references it
        // gets the satellite factory namespace globally, alongside the core pair.
        var src = """
                  namespace Demo;
                  public class NotAComponent { }
                  """;

        var output = Run(src);

        Assert.Contains("global using static global::Rask.Native.Components.Generated;", output);
        Assert.Contains("global using static global::Rask.Core.Components.Generated;", output);
    }

    [Fact]
    public void NoMarkerAssemblyReferenced_NoDanglingFactoryGlobalUsing()
    {
        // A consumer that does NOT reference a marker-bearing assembly (Rask.Native filtered out) must not
        // get a dangling `using static Rask.Native.Components.Generated;` — the core pair still emits.
        var src = """
                  namespace Demo;
                  public class NotAComponent { }
                  """;

        var refsWithoutNative = GeneratorDriverFixture.BuildReferences()
            .Where(r => r.Display is null || !r.Display.Contains("Rask.Native", StringComparison.Ordinal))
            .ToImmutableArray();

        var output = Run(src, null, refsWithoutNative);

        Assert.DoesNotContain("global using static global::Rask.Native.Components.Generated;", output);
        Assert.Contains("global using static global::Rask.Core.Components.Generated;", output);
    }

    private static string Run(string source) => Run(source, null);

    private static string Run(string source, Dictionary<string, string>? buildProps) =>
        Run(source, buildProps, GeneratorDriverFixture.BuildReferences());

    private static string Run(string source, Dictionary<string, string>? buildProps,
        ImmutableArray<MetadataReference> references)
    {
        var generated = RunRaw(source, buildProps, references);
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

    private static ImmutableArray<GeneratedSourceResult> RunRaw(string source, Dictionary<string, string>? buildProps) =>
        RunRaw(source, buildProps, GeneratorDriverFixture.BuildReferences());

    private static ImmutableArray<GeneratedSourceResult> RunRaw(string source, Dictionary<string, string>? buildProps,
        ImmutableArray<MetadataReference> references)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
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
