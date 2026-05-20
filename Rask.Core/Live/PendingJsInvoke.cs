namespace Rask.Core.Live;

/// <summary>
///     One queued global-JS interop call originating from <c>IJSRuntime.InvokeAsync&lt;T&gt;</c>.
///     <para>
///         Distinct from <see cref="ScopedJsInvoke" />: scoped invokes target a function name
///         on a scope-bound module (<c>methods[method](el, …)</c>), while these resolve a
///         dotted identifier against <c>window</c> (<c>"sessionStorage.getItem"</c>) and are
///         tracked by the <see cref="Microsoft.JSInterop.JSRuntime" /> base class's own
///         pending-task store rather than Rask's <see cref="JsInvokeResultStore" />.
///     </para>
///     <para>
///         <see cref="ResultType" /> mirrors <c>Microsoft.JSInterop.JSCallResultType</c>
///         (0=Default, 1=JSVoidResult, 2=JSObjectReference, 3=JSStreamReference). Kept as a
///         plain int so this type can live in Rask.Core without dragging in the
///         <c>Microsoft.JSInterop</c> package — only Rask.Server's <c>RaskJSRuntime</c>
///         references the package.
///     </para>
/// </summary>
public readonly record struct PendingJsInvoke(
    long TaskId,
    string Identifier,
    string? ArgsJson,
    int ResultType,
    long TargetInstanceId);
