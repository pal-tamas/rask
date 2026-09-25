namespace Rask.Site.DataDemo;

/// <summary>Completes once the model surface is configured and the table, its index and the seed rows exist.</summary>
/// <remarks>
///     A browser app starts its hosted services after the first render, so the page asks this before its first read
///     rather than racing the schema. A failure is kept, so the page can say what broke instead of spinning.
/// </remarks>
public sealed class NotesReady
{
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes when the database is ready, or faults with why it is not.</summary>
    public Task Task => _ready.Task;

    internal void Set() => _ready.TrySetResult();

    internal void Fail(Exception error) => _ready.TrySetException(error);
}
