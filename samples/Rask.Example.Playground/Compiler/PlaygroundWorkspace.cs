using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;

namespace Rask.Example.Playground.Compiler;

/// <summary>
///     The IDE brain behind the editor: turns the code in the buffer into as-you-type squiggles
///     (<see cref="DiagnoseAsync" />) and IntelliSense suggestions (<see cref="CompleteAsync" />) using the
///     same Roslyn pipeline the Run button uses — but it <b>never Emits or Assembly.Loads</b>. That matters:
///     Mono WASM can't unload a loaded assembly, so analysing on every keystroke through the Run path would
///     leak an assembly per stroke. This path binds and queries only, so typing is free.
/// </summary>
/// <remarks>
///     Completion needs a Roslyn <c>Document</c> in a workspace whose host has the C# <em>Features</em>
///     services (that's where <c>CompletionService</c> lives). A single <see cref="AdhocWorkspace" /> is
///     created with a MEF host that adds the Features assemblies to the default set; each request forks a
///     throwaway solution off it (never applied) so nothing accumulates. The user document is compiled
///     alongside the Rask generator's output trees — the <c>Generated.*</c> factories and the two
///     <c>global using static</c> directives — so the terse <c>Div()[…]</c> forms resolve for completion and
///     diagnostics exactly as they do at Run.
/// </remarks>
public sealed class PlaygroundWorkspace : IDisposable
{
    private const string AnalysisAssemblyName = "RaskPlaygroundAnalysis";

    private static readonly CSharpCompilationOptions CompilationOptions =
        new(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable);

    private readonly IReadOnlyList<MetadataReference> _references;
    private readonly AdhocWorkspace _workspace;

    public PlaygroundWorkspace(IReadOnlyList<MetadataReference> references)
    {
        _references = references;
        _workspace = new AdhocWorkspace(CreateHostServices());
    }

    public void Dispose() => _workspace.Dispose();

    /// <summary>
    ///     Bind the snippet and return every visible compiler/generator/analyzer diagnostic for the user's
    ///     own code — the live-squiggle feed. No Emit, no Assembly.Load, so it's safe to call on every edit.
    /// </summary>
    public async Task<IReadOnlyList<PlaygroundDiagnostic>> DiagnoseAsync(
        string source, CancellationToken cancellationToken = default)
    {
        var compilation = PlaygroundCompilation.Create(source, _references, AnalysisAssemblyName, cancellationToken);

        var compilationDiagnostics = compilation.Output.GetDiagnostics(cancellationToken);

        var diagnostics = new List<PlaygroundDiagnostic>();
        DiagnosticMapper.Collect(compilation.GeneratorDiagnostics, compilation.UserTree, diagnostics);
        DiagnosticMapper.Collect(compilationDiagnostics, compilation.UserTree, diagnostics);
        await DiagnosticMapper
            .AppendAnalyzerDiagnosticsAsync(compilation.Output, compilation.UserTree, diagnostics, cancellationToken)
            .ConfigureAwait(false);

        return diagnostics;
    }

    /// <summary>
    ///     IntelliSense at <paramref name="position" /> (a 0-based UTF-16 offset into <paramref name="source" />,
    ///     what Monaco's <c>model.getOffsetAt</c> yields). Returns the flat suggestion list; an empty list when
    ///     the C# completion service is unavailable or the position yields nothing.
    /// </summary>
    public async Task<IReadOnlyList<PlaygroundCompletion>> CompleteAsync(
        string source, int position, CancellationToken cancellationToken = default)
    {
        var compilation = PlaygroundCompilation.Create(source, _references, AnalysisAssemblyName, cancellationToken);
        var document = CreateUserDocument(compilation, source);

        var completionService = CompletionService.GetService(document);
        if (completionService is null)
        {
            return Array.Empty<PlaygroundCompletion>();
        }

        var clamped = Math.Clamp(position, 0, source.Length);
        var completions = await completionService
            .GetCompletionsAsync(document, clamped, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var items = completions.ItemsList;
        var result = new List<PlaygroundCompletion>(items.Count);
        foreach (var item in items)
        {
            var insert = string.IsNullOrEmpty(item.FilterText) ? item.DisplayText : item.FilterText;
            result.Add(new PlaygroundCompletion(
                Label: item.DisplayText,
                Kind: item.Tags.IsDefaultOrEmpty ? "Text" : item.Tags[0],
                InsertText: insert,
                SortText: string.IsNullOrEmpty(item.SortText) ? item.DisplayText : item.SortText,
                Detail: string.IsNullOrEmpty(item.InlineDescription) ? null : item.InlineDescription));
        }

        return result;
    }

    // A throwaway document for one completion request: fork a fresh project off the workspace's (empty)
    // solution and DON'T apply it, so the workspace never accumulates. The generator output trees ride along
    // as auto-generated documents so `Generated.*` + the global usings are in scope at the caret.
    private Document CreateUserDocument(PlaygroundCompilation compilation, string source)
    {
        var projectId = ProjectId.CreateNewId();
        var projectInfo = ProjectInfo.Create(
            projectId,
            VersionStamp.Default,
            AnalysisAssemblyName,
            AnalysisAssemblyName,
            LanguageNames.CSharp,
            compilationOptions: CompilationOptions,
            parseOptions: PlaygroundCompilation.ParseOptions,
            metadataReferences: _references);

        var solution = _workspace.CurrentSolution.AddProject(projectInfo);

        foreach (var tree in compilation.GeneratedTrees())
        {
            solution = solution.AddDocument(
                DocumentId.CreateNewId(projectId), "Generated.cs", tree.GetText(), filePath: "Generated.g.cs");
        }

        var userDocId = DocumentId.CreateNewId(projectId);
        solution = solution.AddDocument(userDocId, "Playground.cs", SourceText.From(source));

        return solution.GetDocument(userDocId)!;
    }

    // CompletionService lives in Microsoft.CodeAnalysis.Features and its C# implementation in
    // Microsoft.CodeAnalysis.CSharp.Features — neither is in MefHostServices.DefaultAssemblies (which carries
    // only the Workspaces layer). Add both so an AdhocWorkspace can resolve the C# completion service.
    private static MefHostServices CreateHostServices()
    {
        var assemblies = MefHostServices.DefaultAssemblies
            .Concat(FeatureAssemblies())
            .Distinct()
            .ToList();
        return MefHostServices.Create(assemblies);
    }

    private static IEnumerable<Assembly> FeatureAssemblies()
    {
        // typeof(CompletionService) pins Features without a fragile name; the C# implementation assembly has
        // no public type to pin, so it's loaded by name (it's shipped — the app references CSharp.Features).
        yield return typeof(CompletionService).Assembly;

        Assembly? csharpFeatures = null;
        try
        {
            csharpFeatures = Assembly.Load("Microsoft.CodeAnalysis.CSharp.Features");
        }
        catch
        {
            // Left null — completion degrades to none rather than throwing; diagnostics still work.
        }

        if (csharpFeatures is not null)
        {
            yield return csharpFeatures;
        }
    }
}
