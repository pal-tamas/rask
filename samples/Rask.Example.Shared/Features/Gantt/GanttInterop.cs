using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;

namespace Rask.Example.Shared.Features;

/// <summary>
///     Routes a chart event pushed from frappe-gantt back to the <see cref="Gantt" /> instance that owns
///     it, keyed by an id handed to JS at mount. Modelled on the framework's own browser wrappers
///     (<c>Rask.Core.Browser.ResizeInterop</c>): a <b>static</b> <c>[JSInvokable]</c>, because the JS
///     shim only exposes <c>DotNet.invokeMethodAsync(assembly, name, ...)</c> — there is no
///     <c>DotNetObjectReference</c> handle to dispatch against, so the id is what makes it per-instance.
/// </summary>
/// <remarks>
///     <para>
///         Every parameter here is a primitive or a <see cref="string" /> on purpose. The WASM showcase
///         publishes trimmed (<c>TrimMode=full</c>) with the trim and AOT analyzers on as errors, and
///         marshalling a complex type across the boundary is exactly what trips them. Dates arrive as
///         ISO strings and are parsed invariantly on this side.
///     </para>
///     <para>
///         <b>The token is a capability, so it is unguessable.</b> A <c>[JSInvokable]</c> is callable by
///         any script on the page with any arguments it likes, and on the Server host this registry is
///         static — one dictionary behind every live session. A sequential <c>int</c> id would therefore
///         let one visitor drive another visitor's chart by counting from 1 (a devtools one-liner), so
///         the key is a random token instead: holding it is what proves you own that chart. Always
///         <see cref="Unregister" /> on unmount, or the entry — and the component it captures — leaks for
///         the life of the process.
///     </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class GanttInterop
{
    private static readonly ConcurrentDictionary<string, Gantt> Charts = new(StringComparer.Ordinal);

    internal static string Register(Gantt chart)
    {
        var id = Guid.NewGuid().ToString("N");
        Charts[id] = chart;
        return id;
    }

    internal static void Unregister(string id) => Charts.TryRemove(id, out _);

    /// <summary>Infrastructure. Invoked by <c>Gantt.js</c> when a bar is clicked; do not call.</summary>
    [JSInvokable("RaskGanttTaskClicked")]
    public static Task TaskClicked(string id, string taskId) =>
        Charts.TryGetValue(id, out var chart) ? chart.HandleTaskClickAsync(taskId) : Task.CompletedTask;

    /// <summary>Infrastructure. Invoked by <c>Gantt.js</c> when a bar is dragged or resized; do not call.</summary>
    [JSInvokable("RaskGanttDateChanged")]
    public static Task DateChanged(string id, string taskId, string startIso, string endIso) =>
        Charts.TryGetValue(id, out var chart)
            ? chart.HandleDateChangeAsync(taskId, startIso, endIso)
            : Task.CompletedTask;

    /// <summary>Infrastructure. Invoked by <c>Gantt.js</c> when a bar's progress handle moves; do not call.</summary>
    [JSInvokable("RaskGanttProgressChanged")]
    public static Task ProgressChanged(string id, string taskId, double progress) =>
        Charts.TryGetValue(id, out var chart)
            ? chart.HandleProgressChangeAsync(taskId, progress)
            : Task.CompletedTask;
}

// The wire shapes frappe-gantt expects, serialized through a source-generated context so the WASM
// trimmer can see them. These mirror the library's own option/task names (snake_case, ISO dates) —
// the typed C# surface in Gantt.cs is what callers actually use.
internal sealed record GanttJsTask(string Id, string Name, string Start, string End, double Progress);

internal sealed record GanttJsOptions(
    string ViewMode,
    IReadOnlyList<GanttJsTask> Tasks,
    IReadOnlyList<GanttJsHoliday> Holidays);

internal sealed record GanttJsHoliday(string Date, string Label);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(GanttJsOptions))]
internal sealed partial class GanttJsonContext : JsonSerializerContext;
