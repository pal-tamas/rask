using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rask.Generators.Tests;

// An event is declared `public Callback<int> OnRate { get; set; }` — non-nullable, no initializer — and fired
// with `await OnRate.Invoke(n);`. Any other non-nullable property with no initializer is a REQUIRED step
// (RASK001); a callback must not be, because its default is an unset slot whose Invoke does nothing.
public class NonNullableCallbackTests
{
    private const string Widget = """
                                  using System;
                                  using System.Threading.Tasks;
                                  using Rask.Core;
                                  namespace Demo;
                                  public partial class Rater : Component
                                  {
                                      public Callback OnPick { get; set; }
                                      public Callback<int> OnRate { get; set; }
                                      public Callback<int, string> OnNote { get; set; }

                                      protected override Component? Render() => Div;

                                      public async Task Rate(int n) => await OnRate.Invoke(n);
                                  }
                                  """;

    [Fact]
    public void A_non_nullable_callback_with_no_initializer_raises_no_Rask001()
    {
        var run = BuilderGeneratorHarness.Run(Widget);

        var required = run.WithId("RASK001");

        Assert.Empty(required);
    }

    [Fact]
    public void A_component_whose_callbacks_are_left_unset_compiles()
    {
        var source = Widget + """

                              public sealed partial class Page : Component
                              {
                                  protected override Component? Render() => Div[Rater, Rater.OnRate(n => { _ = n; })];
                              }
                              """;

        var compilation = Compile(source);

        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    // The same steps a `Callback<int>?` gets: the sync shape, the async shape, and a pass-through that
    // still takes `null` for "no handler" — which a non-nullable struct parameter could not.
    [Theory]
    [InlineData("OnRate(v => { _ = v; })", "System.Action<int>?")]
    [InlineData("OnRate(async v => { await Task.Yield(); _ = v; })", "System.Func<int, System.Threading.Tasks.Task>?")]
    [InlineData("OnPick(() => { })", "System.Action?")]
    [InlineData("OnPick(null)", "Rask.Core.Callback?")]
    [InlineData("OnRate(default(Callback<int>))", "Rask.Core.Callback<int>?")]
    [InlineData("OnNote((n, s) => { _ = n; _ = s; })", "System.Action<int, string>?")]
    public void A_non_nullable_callback_gets_the_same_steps_as_a_nullable_one(string step, string expectedParameter)
    {
        var source = Widget + $$"""

                                public sealed partial class Page : Component
                                {
                                    protected override Component? Render() => Rater.{{step}};
                                }
                                """;

        var compilation = Compile(source);

        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        var tree = compilation.SyntaxTrees.First(t => t.ToString().Contains("class Page", StringComparison.Ordinal));
        var page = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
            .First(c => c.Identifier.Text == "Page");
        var call = page.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .First(i => i.Expression is MemberAccessExpressionSyntax);
        var bound = (IMethodSymbol)compilation.GetSemanticModel(tree).GetSymbolInfo(call).Symbol!;
        Assert.Equal(expectedParameter, bound.Parameters[^1].Type.ToDisplayString());
    }

    // A `Callback<T>?` a component author already wrote keeps working exactly as it did.
    [Fact]
    public void A_nullable_callback_declared_by_the_author_still_compiles()
    {
        var source = """
                     using System.Threading.Tasks;
                     using Rask.Core;
                     namespace Demo;
                     public partial class Legacy : Component
                     {
                         public Callback<int>? OnRate { get; set; }

                         protected override Component? Render() => Div;

                         public async Task Rate(int n)
                         {
                             if (OnRate?.Invoke(n) is { } pending)
                             {
                                 await pending;
                             }
                         }
                     }
                     public sealed partial class Page : Component
                     {
                         protected override Component? Render() => Div[Legacy, Legacy.OnRate(n => { _ = n; }).OnRate(null)];
                     }
                     """;

        var compilation = Compile(source);

        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    private static CSharpCompilation Compile(string source)
    {
        var run = BuilderGeneratorHarness.Run(source);
        var trees = run.Sources.Select(s => s.SourceText.ToString()).Prepend(source)
            .Select(s => CSharpSyntaxTree.ParseText(s, new CSharpParseOptions(LanguageVersion.Latest)));

        return CSharpCompilation.Create(
            "TestAssembly", trees, GeneratorDriverFixture.BuildReferences(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }
}
