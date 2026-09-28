using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Analyzers;
using Rask.Generators.CodeFixes;

namespace Rask.Generators.Tests;

/// <summary>
///     RASK096. A delegate-typed property is invocable, so `x.OnSave(fn)` binds as a call of it and the chain
///     setter is unreachable. These pin which delegates are events (reported) and which are templates or
///     selectors (not), and what the fix rewrites.
/// </summary>
public class DelegateEventAnalyzerTests
{
    private static string Source(string members) => $$"""
        using System;
        using System.Threading.Tasks;
        using Rask.Core;
        namespace Demo;
        public sealed record Order;
        public partial class Saver : Component
        {
            {{members}}
            protected override Component? Render() => Div;
        }
        """;

    [Theory]
    [InlineData("public Action? OnPick { get; set; }", "Declare 'OnPick' as Callback, not Action")]
    [InlineData("public Action<Order>? OnSave { get; set; }", "Declare 'OnSave' as Callback<Order>, not Action<Order>")]
    [InlineData("public Action<Order> OnSave { get; set; }", "Declare 'OnSave' as Callback<Order>, not Action<Order>")]
    [InlineData("public Action<int, string>? OnNote { get; set; }", "Declare 'OnNote' as Callback<int, string>, not Action<int, string>")]
    [InlineData("public Func<Task>? OnGo { get; set; }", "Declare 'OnGo' as Callback, not Func<Task>")]
    [InlineData("public Func<Order, Task>? OnSave { get; set; }", "Declare 'OnSave' as Callback<Order>, not Func<Order, Task>")]
    [InlineData("public Func<Order, ValueTask>? OnSave { get; set; }", "Declare 'OnSave' as Callback<Order>, not Func<Order, ValueTask>")]
    public async Task An_event_declared_as_a_delegate_is_told_its_callback_type(string member, string message)
    {
        var source = Source(member);

        var diagnostics = await Diagnostics(source);

        var d = Assert.Single(diagnostics);
        Assert.Equal(message, d.GetMessage());
    }

    [Theory]
    [InlineData("public Func<Order, Component>? Template { get; set; }")]
    [InlineData("public Func<Order, string>? Label { get; set; }")]
    [InlineData("public Func<Order, bool>? Filter { get; set; }")]
    [InlineData("public Func<Order, Task<bool>>? CanSave { get; set; }")]
    [InlineData("public Action<int, int, int>? OnThree { get; set; }")]
    [InlineData("public Callback<Order> OnSave { get; set; }")]
    [InlineData("public Callback<Order>? OnSave { get; set; }")]
    [InlineData("internal Action? OnPick { get; set; }")]
    [InlineData("public Action? OnPick { get; init; }")]
    [InlineData("[SkipFactory] public Action? OnPick { get; set; }")]
    public async Task A_template_a_selector_or_a_callback_is_left_alone(string member)
    {
        var source = Source(member);

        var diagnostics = await Diagnostics(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task A_delegate_on_a_class_that_is_not_a_component_is_left_alone()
    {
        var source = """
                     using System;
                     namespace Demo;
                     public sealed class Options { public Action? OnReady { get; set; } }
                     """;

        var diagnostics = await Diagnostics(source);

        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("public Action? OnPick { get; set; }", "public Callback OnPick { get; set; }")]
    [InlineData("public Action<Order>? OnSave { get; set; }", "public Callback<Order> OnSave { get; set; }")]
    [InlineData("public Func<Order, Task>? OnSave { get; set; }", "public Callback<Order> OnSave { get; set; }")]
    [InlineData("public System.Action<int, string> OnNote { get; set; }", "public Callback<int, string> OnNote { get; set; }")]
    public async Task The_fix_declares_the_non_nullable_callback(string written, string meant)
    {
        var source = Source(written);

        var fixedSource = await Fix(source);

        Assert.Contains(meant, fixedSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_fix_fires_the_callback_and_awaits_it_where_the_method_is_async()
    {
        var source = Source("""
            public Action<Order>? OnSave { get; set; }
            public async Task Save(Order order)
            {
                await Task.Yield();
                OnSave?.Invoke(order);
            }
            public async Task SaveAgain(Order order)
            {
                await Task.Yield();
                OnSave(order);
            }
            public void SaveNow(Order order) => OnSave?.Invoke(order);
            """);

        var fixedSource = await Fix(source);

        Assert.Equal(2, fixedSource.Split("await OnSave.Invoke(order);").Length - 1);
        Assert.Contains("public void SaveNow(Order order) => OnSave.Invoke(order);", fixedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("?.Invoke", fixedSource, StringComparison.Ordinal);
    }

    private static Task<string> Fix(string source) =>
        CodeFixHarness.ApplyAnalyzerFixAsync(
            new DelegateEventAnalyzer(), new DelegateEventCodeFixProvider(), "RASK096", source);

    private static async Task<ImmutableArray<Diagnostic>> Diagnostics(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            GeneratorDriverFixture.BuildReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var all = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new DelegateEventAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();

        return all.Where(d => d.Id == "RASK096").ToImmutableArray();
    }
}
