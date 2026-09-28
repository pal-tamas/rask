namespace Rask.Core.Forms;

/// <summary>
///     The asynchronous counterpart of <see cref="Validate{T}" />: given the current value and a
///     cancellation token, returns the error messages once any async work (a server round-trip, say)
///     completes. Selected over the sync overload by the two-argument lambda shape at the call site.
/// </summary>
/// <remarks>
///     Latest-write-wins cancellation is driven by the <see cref="EditContext" />; honour the supplied
///     <see cref="CancellationToken" /> and let <see cref="OperationCanceledException" /> propagate so a
///     superseded check is dropped without surfacing a message.
/// </remarks>
public delegate ValueTask<IEnumerable<string>> ValidateAsync<in T>(T value, CancellationToken cancellationToken);
