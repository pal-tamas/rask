using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Rask.Generators.Tests;

// Drives a CodeFixProvider end-to-end: build a Document over the same references the generator tests
// use, obtain the target diagnostic (from an analyzer or from a source generator so its Location sits
// in the Document's tree), register the fix, apply the first CodeAction, and return the rewritten text.
internal static class CodeFixHarness
{
    // Applies the fix for an analyzer-produced diagnostic (e.g. RASK023).
    public static async Task<string> ApplyAnalyzerFixAsync(
        DiagnosticAnalyzer analyzer, CodeFixProvider provider, string diagnosticId, string source)
    {
        var document = CreateDocument(source);
        var compilation = (CSharpCompilation)(await document.Project.GetCompilationAsync())!;
        var diagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create(analyzer))
            .GetAnalyzerDiagnosticsAsync();
        return await ApplyAsync(provider, document, FirstOf(diagnostics, diagnosticId));
    }

    // Applies the fix for a source-generator-produced diagnostic (e.g. RASK001).
    public static async Task<string> ApplyGeneratorFixAsync(
        IIncrementalGenerator generator, CodeFixProvider provider, string diagnosticId, string source)
    {
        var (document, diagnostic) = await GeneratorDiagnosticAsync(generator, diagnosticId, source);
        return await ApplyAsync(provider, document, diagnostic);
    }

    // True when the provider offers a fix for a generator diagnostic — lets a test assert that a fix is
    // deliberately withheld (e.g. the RASK001 fix is suppressed for a DI-constructor component).
    public static async Task<bool> IsGeneratorFixOfferedAsync(
        IIncrementalGenerator generator, CodeFixProvider provider, string diagnosticId, string source)
    {
        var (document, diagnostic) = await GeneratorDiagnosticAsync(generator, diagnosticId, source);
        return (await CollectActionsAsync(provider, document, diagnostic)).Count > 0;
    }

    private static async Task<(Document, Diagnostic)> GeneratorDiagnosticAsync(
        IIncrementalGenerator generator, string diagnosticId, string source)
    {
        var document = CreateDocument(source);
        var compilation = (CSharpCompilation)(await document.Project.GetCompilationAsync())!;
        var runResult = CSharpGeneratorDriver
            .Create(generator)
            .WithUpdatedParseOptions(new CSharpParseOptions(LanguageVersion.Latest))
            .RunGenerators(compilation)
            .GetRunResult();
        var diagnostics = runResult.Diagnostics
            .Concat(runResult.Results.SelectMany(r => r.Diagnostics))
            .ToImmutableArray();
        return (document, FirstOf(diagnostics, diagnosticId));
    }

    private static Diagnostic FirstOf(ImmutableArray<Diagnostic> diagnostics, string id)
    {
        var match = diagnostics.FirstOrDefault(d => d.Id == id);
        Assert.True(match is not null, $"Expected a {id} diagnostic; got [{string.Join(", ", diagnostics.Select(d => d.Id))}]");
        return match!;
    }

    private static async Task<List<CodeAction>> CollectActionsAsync(
        CodeFixProvider provider, Document document, Diagnostic diagnostic)
    {
        var actions = new List<CodeAction>();
        var context = new CodeFixContext(
            document, diagnostic, (action, _) => actions.Add(action), CancellationToken.None);
        await provider.RegisterCodeFixesAsync(context);
        return actions;
    }

    private static async Task<string> ApplyAsync(CodeFixProvider provider, Document document, Diagnostic diagnostic)
    {
        var actions = await CollectActionsAsync(provider, document, diagnostic);
        Assert.NotEmpty(actions);

        var operations = await actions[0].GetOperationsAsync(CancellationToken.None);
        var applied = operations.OfType<ApplyChangesOperation>().Single();
        var changedDocument = applied.ChangedSolution.GetDocument(document.Id)!;
        var text = await changedDocument.GetTextAsync();
        return text.ToString();
    }

    private static Document CreateDocument(string source)
    {
        var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(
            ProjectId.CreateNewId(),
            VersionStamp.Create(),
            "Test",
            "Test",
            LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable),
            parseOptions: new CSharpParseOptions(LanguageVersion.Latest),
            metadataReferences: GeneratorDriverFixture.BuildReferences()));
        return workspace.AddDocument(project.Id, "Test.cs", SourceText.From(source));
    }
}
