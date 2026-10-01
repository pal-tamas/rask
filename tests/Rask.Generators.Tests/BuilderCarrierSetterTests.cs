using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rask.Generators.Tests;

// A carrier-typed prop (Callback/Fn/Validator) holds a delegate WITHOUT being one, which is what lets a
// step keep the property's name. The cost is that a lambda can never reach it — a lambda has no type, so
// no user-defined conversion applies (CS1660) — so the step has to offer the bare delegate shapes as
// overloads. These pin that the overloads exist, that they bind the way a call site expects, and that a
// carrier is treated as a delegate by the propsChanged fold.
public class BuilderCarrierSetterTests
{
    private const string Src = """
                               using System;
                               using System.Threading.Tasks;
                               using Rask.Core;
                               namespace Demo;
                               public partial class Widget : Component
                               {
                                   public Callback? OnPick { get; set; }
                                   public Callback<int>? OnRate { get; set; }
                                   public Fn<int, string>? Row { get; set; }
                                   public Validator<int>? Check { get; set; }
                               }
                               """;

    [Fact]
    public void A_carrier_setter_offers_the_bare_sync_and_async_shapes()
    {
        var output = BuilderGeneratorHarness.Run(Src).Source("RaskBuilderSetters.g.cs");

        Assert.Contains("__b, global::System.Action? value)", output, StringComparison.Ordinal);
        Assert.Contains(
            "__b, global::System.Func<global::System.Threading.Tasks.Task>? value)", output,
            StringComparison.Ordinal);
        Assert.Contains("__b, global::System.Action<int>? value)", output, StringComparison.Ordinal);
        Assert.Contains(
            "__b, global::System.Func<int, global::System.Threading.Tasks.Task>? value)", output,
            StringComparison.Ordinal);
    }

    // Fn returns a value and has no async twin, so its last type argument is the RETURN type: one
    // overload, not two.
    [Fact]
    public void A_value_carrier_offers_one_shape_and_it_returns()
    {
        var output = BuilderGeneratorHarness.Run(Src).Source("RaskBuilderSetters.g.cs");

        Assert.Contains("__b, global::System.Func<int, string>? value)", output, StringComparison.Ordinal);
    }

    // The third family. A rule RETURNS the messages that reject the value, so neither of the other two
    // carriers fits it: `Callback` has no return and `Fn` has no async twin. Its sync shape is the
    // framework's own `Validate<T>`, and the async one reads the field's token from the ambient scope —
    // which is exactly why it needed a carrier of its own rather than a wider `Fn`.
    [Fact]
    public void A_validator_carrier_offers_the_sync_rule_and_the_async_rule()
    {
        var output = BuilderGeneratorHarness.Run(Src).Source("RaskBuilderSetters.g.cs");

        Assert.Contains("__b, global::Rask.Core.Forms.Validate<int>? value)", output, StringComparison.Ordinal);
        Assert.Contains("__b, global::System.Func<int, global::System.Threading.Tasks.ValueTask<global::System.Collections.Generic.IEnumerable<string>>>? value)", output, StringComparison.Ordinal);
        Assert.DoesNotContain("global::System.Threading.CancellationToken, global::System.Threading.Tasks.ValueTask", output, StringComparison.Ordinal);
    }

    // `null` converts to the carrier AND to every delegate overload, so without the priority the blessed
    // `.OnPick(null)` spelling is CS0121.
    [Fact]
    public void The_carrier_typed_setter_wins_a_null_argument()
    {
        var output = BuilderGeneratorHarness.Run(Src).Source("RaskBuilderSetters.g.cs");

        Assert.Contains(
            "[global::System.Runtime.CompilerServices.OverloadResolutionPriority(1)]", output,
            StringComparison.Ordinal);
    }

    // The quietest regression this file guards: a `Callback?` is `Nullable<Callback>`, a STRUCT, so a
    // TypeKind-only delegate test folds it. Nothing would fail — every element carrying a handler would
    // simply report propsChanged: true on every frame, defeating the render cache tree-wide.
    [Fact]
    public void A_carrier_does_not_fold_into_props_changed()
    {
        var output = BuilderGeneratorHarness.Run(Src).Source("RaskBuilderSetters.g.cs");

        var pick = output.Split('\n').Where(l => l.Contains("OnPick", StringComparison.Ordinal)).ToArray();

        Assert.NotEmpty(pick);
        Assert.All(pick, line => Assert.DoesNotContain("BuilderRuntime.Track(", line, StringComparison.Ordinal));
    }

    // The point of the whole exercise: one name, and the call site writes the handler it means.
    [Theory]
    [InlineData("OnPick(() => { })", "System.Action?")]
    [InlineData("OnPick(async () => { await Task.Yield(); })", "System.Func<System.Threading.Tasks.Task>?")]
    [InlineData("OnPick(() => Task.CompletedTask)", "System.Func<System.Threading.Tasks.Task>?")]
    [InlineData("OnRate(v => { _ = v; })", "System.Action<int>?")]
    [InlineData("OnRate(async v => { await Task.Yield(); _ = v; })", "System.Func<int, System.Threading.Tasks.Task>?")]
    [InlineData("Row(i => i.ToString())", "System.Func<int, string>?")]
    // A rule takes either shape under the one name, as a Callback does: `v => …` is the synchronous
    // rule and `async v => …` the asynchronous one, whose token is ambient. An async lambda cannot
    // convert to the synchronous delegate, and a synchronous body cannot convert to a ValueTask, so
    // each lands on exactly one.
    [InlineData("Check(v => new[] { v.ToString() })", "Rask.Core.Forms.Validate<int>?")]
    [InlineData("Check(async v => { await Task.Yield(); return new[] { v.ToString() }; })", "System.Func<int, System.Threading.Tasks.ValueTask<System.Collections.Generic.IEnumerable<string>>>?")]
    [InlineData("Check(null)", "Rask.Core.Validator<int>?")]
    [InlineData("OnPick(null)", "Rask.Core.Callback?")]
    public void A_handler_binds_to_the_shape_it_was_written_as(string step, string expectedParameter)
    {
        var source = $$"""
            using System;
            using System.Threading.Tasks;
            using Rask.Core;
            namespace Demo;
            public partial class Widget : Component
            {
                public Callback? OnPick { get; set; }
                public Callback<int>? OnRate { get; set; }
                public Fn<int, string>? Row { get; set; }
                public Validator<int>? Check { get; set; }
            }
            public sealed partial class Page : Component
            {
                protected override Component? Render() => Widget.{{step}};
            }
            """;

        var compilation = Compile(source);

        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error));

        var tree = compilation.SyntaxTrees.First(t => t.ToString().Contains("class Page", StringComparison.Ordinal));
        var model = compilation.GetSemanticModel(tree);
        var call = tree.GetRoot(TestContext.Current.CancellationToken).DescendantNodes().OfType<InvocationExpressionSyntax>()
            .First(i => i.Expression is MemberAccessExpressionSyntax);
        var bound = (IMethodSymbol)model.GetSymbolInfo(call, cancellationToken: TestContext.Current.CancellationToken).Symbol!;

        Assert.Equal(expectedParameter, bound.Parameters[^1].Type.ToDisplayString());
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
