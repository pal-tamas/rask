namespace Rask.Cqrs;

/// <summary>
///     Runs a query's or a command's handler off the thread that draws the page. The call site does not
///     change: <c>await Dispatcher.Query(new ReadFuelings(bytes))</c>.
/// </summary>
/// <remarks>
///     <para>
///         In a browser app the message goes to one Web Worker that runs a second .NET runtime beside the
///         page, so a long synchronous handler — <c>Xlsx.Read</c> of a large workbook, a big XML parse —
///         no longer freezes the page while it works. The worker starts the first time such a message is
///         sent (or at boot, with <c>c.OffThread.Warm = true</c>), and the message and its answer cross as
///         the generated wire encoding, with a <see cref="byte" /> array moved as binary rather than base64.
///         Cancelling the call cancels <c>Current.Cancellation</c> inside the handler; a handler that never
///         looks at it is stopped by restarting the worker.
///     </para>
///     <para>
///         On the server the handler runs on the thread pool, so the page's live session stays responsive
///         while it works.
///     </para>
///     <para>
///         The worker has only what a handler can carry across: the app's Rask.Cqrs handlers, its own
///         <see cref="HttpClient" />, and no page — no DOM, no <c>IJSRuntime</c>, no signed-in user. A
///         handler that asks for a page service, or a message whose result has no wire encoding, is a build
///         error (RASK119).
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class OffThreadAttribute : Attribute;
