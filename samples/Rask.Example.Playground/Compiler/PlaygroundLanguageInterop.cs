using System.ComponentModel;
using System.Text.Json;
using Microsoft.JSInterop;

namespace Rask.Example.Playground.Compiler;

/// <summary>
///     The JS→.NET bridge for the editor's live language features. Monaco (running in JS) asks for
///     completions and diagnostics as the user types; those requests arrive here via
///     <c>window.DotNet.invokeMethodAsync("Rask.Example.Playground", …)</c> — the same static-<see cref="JSInvokable" />
///     dispatch the framework's own browser wrappers use, so no <c>DotNetObjectReference</c> is marshalled.
///     There is exactly one editor, so a single static <see cref="Workspace" /> (set by
///     <c>PlaygroundView</c> once the framework references finish downloading) backs both calls. Until it's
///     set, both return an empty JSON array — the editor simply has no squiggles/suggestions yet.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class PlaygroundLanguageInterop
{
    private static readonly string EmptyArray = "[]";

    /// <summary>The live analysis engine, or null until the framework references have loaded.</summary>
    public static PlaygroundWorkspace? Workspace { get; set; }

    /// <summary>Infrastructure. IntelliSense at a caret offset; returns a JSON array of suggestions. Do not call.</summary>
    [JSInvokable("PlaygroundComplete")]
    public static async Task<string> CompleteAsync(string code, int position)
    {
        if (Workspace is not { } workspace)
        {
            return EmptyArray;
        }

        var completions = await workspace.CompleteAsync(code, position).ConfigureAwait(false);
        return JsonSerializer.Serialize(completions.Select(c => new
        {
            label = c.Label,
            kind = c.Kind,
            insertText = c.InsertText,
            sortText = c.SortText,
            detail = c.Detail
        }));
    }

    /// <summary>Infrastructure. As-you-type diagnostics; returns a JSON array of editor markers. Do not call.</summary>
    [JSInvokable("PlaygroundDiagnose")]
    public static async Task<string> DiagnoseAsync(string code)
    {
        if (Workspace is not { } workspace)
        {
            return EmptyArray;
        }

        var diagnostics = await workspace.DiagnoseAsync(code).ConfigureAwait(false);
        return SerializeDiagnostics(diagnostics);
    }

    /// <summary>The marker JSON shape the editor's <c>setMarkers</c> consumes — shared by the live path and Run.</summary>
    public static string SerializeDiagnostics(IEnumerable<PlaygroundDiagnostic> diagnostics) =>
        JsonSerializer.Serialize(diagnostics.Select(d => new
        {
            id = d.Id,
            severity = d.Severity.ToString(),
            message = d.Message,
            startLine = d.StartLine,
            startColumn = d.StartColumn,
            endLine = d.EndLine,
            endColumn = d.EndColumn
        }));
}
