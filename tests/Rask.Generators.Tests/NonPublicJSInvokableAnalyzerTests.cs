using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Rask.Generators.Analyzers;
using Rask.Generators.CodeFixes;

namespace Rask.Generators.Tests;

/// <summary>
///     RASK121 — a <c>[JSInvokable]</c> method that is not public, which <c>Microsoft.JSInterop</c>'s dispatcher
///     never finds. Analyzed the way a WebAssembly app or a library on <c>Rask</c> alone sees it: without the ASP.NET
///     components assemblies, whose analyzers report the same thing as BL0015.
/// </summary>
public class NonPublicJSInvokableAnalyzerTests
{
    private const string ComponentsAssemblies = "Microsoft.AspNetCore.Components";

    // ---------- it fires ----------

    [Fact]
    public async Task A_private_static_JSInvokable_is_reported()
    {
        var member = "[JSInvokable(\"RaskPing\")] private static Task Ping(int id) => Task.CompletedTask;";

        var d = Assert.Single(await AnalyzeAsync(member));

        Assert.Equal("RASK121", d.Id);
        Assert.Contains("'Ping'", d.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_internal_instance_JSInvokable_is_reported()
    {
        var member = "[JSInvokable] internal void Changed(string value) { }";

        var diagnostics = await AnalyzeAsync(member);

        Assert.Equal("RASK121", Assert.Single(diagnostics).Id);
    }

    // ---------- it stays quiet ----------

    [Fact]
    public async Task A_public_JSInvokable_on_an_internal_class_is_not_reported()
    {
        var member = "[JSInvokable] public static Task Ping() => Task.CompletedTask;";

        var diagnostics = await AnalyzeAsync(member);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task A_private_method_without_the_attribute_is_not_reported()
    {
        var member = "private static Task Ping() => Task.CompletedTask;";

        var diagnostics = await AnalyzeAsync(member);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task A_project_that_sees_the_ASP_NET_components_is_left_to_BL0015()
    {
        var source = Interop("[JSInvokable] private static Task Ping() => Task.CompletedTask;");

        var diagnostics = await CompiledAnalysis.RunAsync(
            Compile(source, GeneratorDriverFixture.BuildReferences()), new NonPublicJSInvokableAnalyzer());

        Assert.Empty(diagnostics);
    }

    // ---------- the quick-fix ----------

    [Fact]
    public async Task The_RASK121_fix_makes_a_private_method_public()
    {
        var source = Interop("[JSInvokable] private static Task Ping() => Task.CompletedTask;");

        var fixedSource = await CodeFixHarness.ApplyAnalyzerFixWithoutAsync(
            new NonPublicJSInvokableAnalyzer(), new NonPublicJSInvokableCodeFixProvider(), "RASK121", source,
            ComponentsAssemblies);

        Assert.Contains("[JSInvokable] public static Task Ping()", fixedSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_RASK121_fix_replaces_both_words_of_protected_internal()
    {
        var source = Interop("[JSInvokable] protected internal void Changed() { }", sealedClass: false);

        var fixedSource = await CodeFixHarness.ApplyAnalyzerFixWithoutAsync(
            new NonPublicJSInvokableAnalyzer(), new NonPublicJSInvokableCodeFixProvider(), "RASK121", source,
            ComponentsAssemblies);

        Assert.Contains("[JSInvokable] public void Changed()", fixedSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_RASK121_fix_adds_public_to_a_method_with_no_access_modifier()
    {
        var source = Interop("[JSInvokable]\n    static Task Ping() => Task.CompletedTask;");

        var fixedSource = await CodeFixHarness.ApplyAnalyzerFixWithoutAsync(
            new NonPublicJSInvokableAnalyzer(), new NonPublicJSInvokableCodeFixProvider(), "RASK121", source,
            ComponentsAssemblies);

        Assert.Contains("\n    public static Task Ping()", fixedSource, StringComparison.Ordinal);
    }

    private static string Interop(string member, bool sealedClass = true) => $$"""
        using System.Threading.Tasks;
        using Microsoft.JSInterop;
        namespace Demo;
        internal {{(sealedClass ? "sealed " : string.Empty)}}class Interop
        {
            {{member}}
        }
        """;

    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string member) =>
        CompiledAnalysis.RunAsync(
            Compile(
                Interop(member),
                GeneratorDriverFixture.BuildReferences().Where(r =>
                    !Path.GetFileName(r.Display ?? string.Empty).StartsWith(ComponentsAssemblies, StringComparison.Ordinal))),
            new NonPublicJSInvokableAnalyzer());

    private static Compilation Compile(string source, IEnumerable<MetadataReference> references) =>
        CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
}
