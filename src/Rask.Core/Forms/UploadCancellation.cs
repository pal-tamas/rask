namespace Rask.Core.Forms;

// The reader called off an upload while its handler was running (ICancellableFiles): the handler is cancelled with
// the files, and that is the end of it. Nothing failed, so nothing is reported and no boundary trips; whatever the
// handler changed before it stopped is drawn, as for any handler.
internal static class UploadCancellation
{
    internal static async Task Run(Component owner, IReadOnlyList<IRaskFile> files, Func<Task> handle)
    {
        if (files.Count == 0 || FileListReader.ResolveBackend() is not ICancellableFiles cancellable)
        {
            await handle().ConfigureAwait(false);
            return;
        }

        // The cancel is a limit on this dispatch, as a host's timeout is: a callback the handler reaches through
        // another component runs under its own owner's lifetime, LINKED with the limit (CallbackOwnerScope), so the
        // limit has to be the dispatch's and not only the ambient token, which that scope replaces.
        var cancelled = cancellable.CancellationOf(files);
        var host = Live.DispatchEventTokenScope.Host;
        using var limit = host.CanBeCanceled ? CancellationTokenSource.CreateLinkedTokenSource(host, cancelled) : null;
        var bound = limit?.Token ?? cancelled;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(Ambient.CancellationToken, bound);
        using var dispatch = Live.DispatchEventTokenScope.Push(owner, bound, linked.Token);
        using var ambient = Ambient.Enter(linked.Token);
        try
        {
            await handle().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancelled.IsCancellationRequested)
        {
            // Called off by the reader: the handler stopped where it was told to.
        }
    }
}
