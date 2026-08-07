using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Rask.Generators.Tests;

// PROTOTYPE — the builder setters carry the propsChanged fold the generated factory computes in one
// shot. The factory can snapshot every prop, assign them all and diff once, because it knows where the
// assignments end; a setter chain does not, so each FOLDING setter accumulates its own delta and the
// parent fires the single NotifyParameters when its Render() returns.
//
// Which props fold has to match the factory exactly (see EmitFactory's foldProps), so these pin both
// halves: a value prop tracks, and Key / delegates / auto-wrapped callbacks do not — folding a fresh
// closure every render would report a change every frame and defeat the render cache outright.
public class BuilderSetterEmissionTests
{
    private const string Src = """
                               using Rask.Core;
                               namespace Demo;
                               public partial class Widget : Component
                               {
                                   public string? Title { get; set; }
                                   public int Count { get; set; }
                                   public System.Action? OnPick { get; set; }
                               }
                               """;

    [Fact]
    public void A_value_setter_folds_into_props_changed()
    {
        var output = Run(Src);

        Assert.Contains(
            "Title(this global::Demo.Widget __c, string? value) "
            + "{ global::Rask.Core.BuilderRuntime.Track(__c, __c.Title, value); __c.Title = value; return __c; }",
            output,
            StringComparison.Ordinal);
        Assert.Contains("global::Rask.Core.BuilderRuntime.Track(__c, __c.Count, value);", output,
            StringComparison.Ordinal);
    }

    // A callback prop is a fresh delegate (and, once wrapped, a fresh closure) on every render, so it
    // is never meaningfully equal to the previous one. The factory leaves it out of the fold; so does
    // the setter, or every callback-taking component would re-render every frame.
    [Fact]
    public void A_callback_setter_does_not_fold()
    {
        var output = Run(Src);

        var line = output.Split('\n').Single(l => l.Contains(" Pick(this ", StringComparison.Ordinal));
        Assert.DoesNotContain("BuilderRuntime.Track", line, StringComparison.Ordinal);
    }

    // Key is a reconciliation identity, not a reactive prop: a changed Key means a different logical
    // item, which mounts fresh rather than firing OnPropsChanged on the old instance.
    [Fact]
    public void The_Key_setter_does_not_fold()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public partial class Widget : Component
                  {
                      public object? Key { get; set; }
                  }
                  """;

        var line = Run(src).Split('\n').Single(l => l.Contains(" Key(this ", StringComparison.Ordinal));

        Assert.DoesNotContain("BuilderRuntime.Track", line, StringComparison.Ordinal);
    }

    // Element's ~93 universal props are emitted ONCE as constrained generic extensions rather than
    // per tag, so they take a different branch of EmitSetter. Only the assembly declaring Element
    // contributes them — hence the source-declared Element here.
    [Fact]
    public void The_shared_generic_setters_fold_the_same_way()
    {
        var src = """
                  namespace Rask.Core;
                  public abstract partial class Element : Component
                  {
                      public string? Class { get; set; }
                      public global::Rask.Core.Callback? OnClick { get; set; }
                  }
                  """;

        var output = Run(src);

        Assert.Contains(
            "public static T Class<T>(this T __c, string? value) where T : global::Rask.Core.Element "
            + "{ global::Rask.Core.BuilderRuntime.Track(__c, __c.Class, value); __c.Class = value; return __c; }",
            output,
            StringComparison.Ordinal);

        var click = output.Split('\n').Single(l => l.Contains(" Click<T>(this ", StringComparison.Ordinal));
        Assert.DoesNotContain("BuilderRuntime.Track", click, StringComparison.Ordinal);
    }

    private static string Run(string source)
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
            .WithUpdatedAnalyzerConfigOptions(new BuilderSurfaceOptionsProvider());

        var generated = driver.RunGenerators(compilation).GetRunResult()
            .Results.SelectMany(r => r.GeneratedSources).ToImmutableArray();

        var match = generated.FirstOrDefault(s =>
            s.HintName.Contains("RaskBuilderSetters.g.cs", StringComparison.Ordinal));
        if (match.SourceText is null)
        {
            throw new InvalidOperationException(
                "No RaskBuilderSetters.g.cs generated. Available: ["
                + string.Join(", ", generated.Select(s => s.HintName)) + "]");
        }

        return match.SourceText.ToString();
    }

    // The builder surface is opt-in (RaskBuilderSurface), so the driver has to say so.
    private sealed class BuilderSurfaceOptionsProvider : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Options();

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;

        private sealed class Options : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, out string value)
            {
                if (key == "build_property.RaskBuilderSurface")
                {
                    value = "true";
                    return true;
                }

                value = string.Empty;
                return false;
            }
        }
    }
}
