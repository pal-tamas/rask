namespace Rask.Core.Live;

/// <summary>
///     One queued JS interop call originating from <c>IJSRuntime.InvokeAsync&lt;T&gt;</c>.
///     The identifier is a dotted path resolved against <c>window</c> on the browser side
///     (e.g. <c>"sessionStorage.getItem"</c> or <c>"Rask.{TypeName}.{method}"</c>); the
///     pending task is owned by the <see cref="Microsoft.JSInterop.JSRuntime" /> base
///     class's task store and completed by <c>DotNetDispatcher.EndInvokeJS</c> when the
///     browser returns a jsResult.
///     <para>
///         <see cref="ResultType" /> mirrors <c>Microsoft.JSInterop.JSCallResultType</c>
///         (0=Default, 1=JSObjectReference, 2=JSStreamReference, 3=JSVoidResult). Kept as a
///         plain int so this type can live in Rask.Core without dragging in the
///         <c>Microsoft.JSInterop</c> package — only Rask.Server's <c>RaskJSRuntime</c>
///         and Rask.Wasm's <c>WasmJSRuntime</c> reference the package.
///     </para>
/// </summary>
public readonly record struct PendingJsInvoke(
    long TaskId,
    string Identifier,
    string? ArgsJson,
    int ResultType,
    long TargetInstanceId);
