using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using Microsoft.JSInterop;
using Rask.Core.Live;

namespace Rask.Example.Shared.Features;

/// <summary>One bar on the chart.</summary>
/// <param name="Id">Stable identity — what the event callbacks report back.</param>
/// <param name="Name">Label drawn on the bar.</param>
/// <param name="Start">First day of the task (inclusive).</param>
/// <param name="End">Last day of the task (inclusive).</param>
/// <param name="Progress">Completion, 0–100.</param>
public sealed record GanttTask(string Id, string Name, DateOnly Start, DateOnly End, double Progress);

/// <summary>A single non-working day, highlighted on the chart.</summary>
/// <param name="Date">The day to highlight.</param>
/// <param name="Label">Shown when the day is hovered.</param>
/// <remarks>
///     A labelled single day, not a range — that is frappe-gantt's own holiday model, and promising a
///     range here would be promising something the library can't draw. Pass consecutive days for a
///     multi-day closure.
/// </remarks>
public sealed record GanttHoliday(DateOnly Date, string Label);

/// <summary>A bar was dragged or resized.</summary>
/// <param name="TaskId">Which task moved.</param>
/// <param name="Start">Its new first day.</param>
/// <param name="End">Its new last day.</param>
/// <remarks>
///     One parameter, not three, and that is load-bearing rather than taste: the generated factory only
///     wraps a callback for auto-re-render when it takes <b>at most one</b> argument. A three-arg delegate
///     is passed through raw, so the owning component would not re-render when it fires and the caller
///     would have to reach for <c>StateHasChanged()</c>. Bundling the arguments keeps the callback on the
///     framework's normal path — which is why nothing in <see cref="GanttDemo" /> calls it.
/// </remarks>
public sealed record GanttDateChange(string TaskId, DateOnly Start, DateOnly End);

/// <summary>A bar's progress handle moved.</summary>
/// <param name="TaskId">Which task changed.</param>
/// <param name="Progress">Its new completion, 0–100.</param>
/// <remarks>One parameter for the same reason as <see cref="GanttDateChange" />.</remarks>
public sealed record GanttProgressChange(string TaskId, double Progress);

/// <summary>Chart zoom level. Maps to frappe-gantt's <c>view_mode</c>.</summary>
public enum GanttViewMode
{
    /// <summary>One column per hour.</summary>
    Hour,

    /// <summary>One column per six hours.</summary>
    QuarterDay,

    /// <summary>One column per twelve hours.</summary>
    HalfDay,

    /// <summary>One column per day. The library's default.</summary>
    Day,

    /// <summary>One column per week.</summary>
    Week,

    /// <summary>One column per month.</summary>
    Month,

    /// <summary>One column per year.</summary>
    Year
}

/// <summary>
///     A Gantt chart, wrapping the third-party <see href="https://github.com/frappe/gantt">frappe-gantt</see>
///     library (MIT, vendored under <c>wwwroot/lib/frappe-gantt</c>). Renders on both transports and on the
///     Native hosts, because everything host-specific is funnelled through <see cref="IJSRuntime" /> and
///     <see cref="RaskLiveOptions.PathBase" />.
/// </summary>
/// <remarks>
///     <para>
///         <b>This is showcase code, not a framework component</b> — it lives in the samples deliberately.
///         It's the worked example for the "third-party libraries" section of <c>docs/js-interop.md</c>:
///         copy it as the recipe for wrapping any DOM-owning JS library in Rask. It is not in
///         <c>Rask.Bootstrap</c> because that package ships zero JavaScript by design.
///     </para>
///     <para>
///         <b>The invariant that makes this safe:</b> the library owns every node inside the host div, and
///         the .NET side renders that div as a childless leaf. The normal diff addresses nodes by
///         positional path from the render tree, so it can never reach inside a leaf. Full-HTML frames
///         (the first interactive frame after page load always is one) are applied by morphing the
///         document, and a morph *would* pair the host's live children against the rendered zero and
///         delete the chart — so <c>Gantt.js</c> tags them <c>data-rask-managed</c>, which the reconciler
///         skips. Keep the host a leaf and keep that tag; drop either and the chart disappears.
///     </para>
/// </remarks>
public sealed partial class Gantt : Component
{
    private readonly ElementRef _host = ElementRef.New();
    private readonly IJSRuntime _js;
    private string? _id;
    private bool _mounted;

    // GanttInterop's [JSInvokable]s are reached only through the JS DotNet dispatcher (reflection), so
    // without this the WASM trimmer would drop them and every chart event would silently do nothing.
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(GanttInterop))]
    public Gantt(IJSRuntime js) => _js = js;

    /// <summary>The bars to draw. Required.</summary>
    public new required IReadOnlyList<GanttTask> Data { get; set; }

    /// <summary>Non-working days to highlight. Weekends are highlighted by the library regardless.</summary>
    public IReadOnlyList<GanttHoliday>? Holidays { get; set; }

    /// <summary>Zoom level. Changing it re-renders the chart in place, keeping scroll position.</summary>
    public GanttViewMode ViewMode { get; set; } = GanttViewMode.Day;

    /// <summary>Raised with the task id when a bar is clicked.</summary>
    public Func<string, Task>? OnTaskClick { get; set; }

    /// <summary>Raised when a bar is dragged or resized.</summary>
    public Func<GanttDateChange, Task>? OnDateChange { get; set; }

    /// <summary>Raised when a bar's progress handle moves.</summary>
    public Func<GanttProgressChange, Task>? OnProgressChange { get; set; }

    // A leaf: no children here, ever. See the class remarks — this is what keeps the diff out of the
    // library's DOM. The visible height comes from Gantt.css.
    protected override Component? Render() => Div.Ref(_host).Class("rask-gantt");

    // Mount in OnRendered, not OnMount: OnMount runs *before* the first render, so the host element
    // doesn't exist yet and the ref would resolve to null on the JS side. Interop issued during a render
    // walk is queued onto the frame and runs after the diff is applied, so the div is committed by then.
    protected override async Task OnRenderedAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        _id = GanttInterop.Register(this);
        var mountedWith = BuildOptionsJson();
        try
        {
            await _js.InvokeVoidAsync("Rask.Gantt.mount", _host, _id, LiveOptions.PathBase, mountedWith);
        }
        catch
        {
            GanttInterop.Unregister(_id);
            _id = null;
            throw;
        }

        _mounted = true;

        // That await spans the library's own download, and a prop change landing inside that window found
        // _mounted still false and skipped its update. Reconcile once here or the chart is left showing
        // the state it mounted with, permanently, until some later unrelated change happens to push.
        var latest = BuildOptionsJson();
        if (!string.Equals(latest, mountedWith, StringComparison.Ordinal))
        {
            await _js.InvokeVoidAsync("Rask.Gantt.update", _host, latest);
        }
    }

    // Fires only on a real prop change, so this is the natural place to push new data at the library
    // rather than re-mounting it (which would lose scroll position).
    protected override async Task OnPropsChangedAsync()
    {
        if (_mounted)
        {
            await _js.InvokeVoidAsync("Rask.Gantt.update", _host, BuildOptionsJson());
        }
    }

    // Teardown is sync and fire-and-forget on purpose. An IAsyncDisposable component is *awaited* by the
    // framework's dispose walk, and that walk also runs when a session is torn down because its socket
    // already closed — at which point an interop call has nobody to answer it and never completes. There
    // is no ambient timeout, so awaiting here would hang the session's disposal forever and leak its DI
    // scope, its whole component tree and its locks: once per visitor, until the process runs out.
    protected override void OnUnmount()
    {
        if (_id is { } id)
        {
            GanttInterop.Unregister(id);
            _id = null;
        }

        if (!_mounted)
        {
            return;
        }

        _mounted = false;
        _ = DestroyQuietlyAsync();
    }

    private async Task DestroyQuietlyAsync()
    {
        try
        {
            await _js.InvokeVoidAsync("Rask.Gantt.destroy", _host);
        }
        catch
        {
            // The client is already gone (tab closed, socket dropped) — its DOM went with it, so there is
            // nothing left to tear down and nothing useful to report. Never let this reach the void.
        }
    }

    internal Task HandleTaskClickAsync(string taskId) => OnTaskClick?.Invoke(taskId) ?? Task.CompletedTask;

    internal Task HandleDateChangeAsync(string taskId, string startIso, string endIso)
    {
        if (OnDateChange is null || !TryParseIsoDate(startIso, out var start) || !TryParseIsoDate(endIso, out var end))
        {
            return Task.CompletedTask;
        }

        return OnDateChange(new GanttDateChange(taskId, start, end));
    }

    internal Task HandleProgressChangeAsync(string taskId, double progress) =>
        OnProgressChange?.Invoke(new GanttProgressChange(taskId, Math.Clamp(progress, 0, 100)))
        ?? Task.CompletedTask;

    // frappe-gantt's view_mode values are its own display strings — two of them have a space, which no
    // enum-name convention would produce. Map explicitly rather than deriving from the name.
    internal static string ToJsViewMode(GanttViewMode mode) => mode switch
    {
        GanttViewMode.Hour => "Hour",
        GanttViewMode.QuarterDay => "Quarter Day",
        GanttViewMode.HalfDay => "Half Day",
        GanttViewMode.Day => "Day",
        GanttViewMode.Week => "Week",
        GanttViewMode.Month => "Month",
        GanttViewMode.Year => "Year",
        _ => "Day"
    };

    // Serialize to a JSON string and hand *that* across, rather than letting the interop layer marshal a
    // complex type — the WASM showcase publishes trimmed, and this keeps the boundary to strings only.
    internal string BuildOptionsJson()
    {
        var payload = new GanttJsOptions(
            ToJsViewMode(ViewMode),
            [.. Data.Select(t => new GanttJsTask(t.Id, t.Name, ToIso(t.Start), ToIso(t.End), t.Progress))],
            [.. (Holidays ?? []).Select(h => new GanttJsHoliday(ToIso(h.Date), h.Label))]);

        return JsonSerializer.Serialize(payload, GanttJsonContext.Default.GanttJsOptions);
    }

    // The WASM showcase runs InvariantGlobalization, so pin the culture rather than inheriting the
    // ambient one — an ISO date is the contract with the library either way.
    private static string ToIso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static bool TryParseIsoDate(string value, out DateOnly date)
    {
        // Gantt.js sends an unzoned wall-clock timestamp and we only model whole days, so keep the date
        // part as written. RoundtripKind (not None) is what stops a value that *does* carry a zone from
        // being converted to this machine's local time, which would land the bar on the wrong day — the
        // server's timezone has no business deciding which column the user dropped a bar on.
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            date = DateOnly.FromDateTime(parsed);
            return true;
        }

        date = default;
        return false;
    }
}
