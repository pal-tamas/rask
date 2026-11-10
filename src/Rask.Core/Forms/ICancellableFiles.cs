namespace Rask.Core.Forms;

// A file backend whose reader can call off an upload while the handler that receives it is still running: the
// browser host, where the handler reads the files out of the reader's tab a chunk at a time and the press on the
// upload's cancel is heard before that handler is done. The handler runs under the token this answers, as its
// ambient cancellation (Ambient.CancellationToken), so a save it awaits stops with the reads.
internal interface ICancellableFiles
{
    /// <summary>The token the reader's cancel of <paramref name="files" /> cancels.</summary>
    CancellationToken CancellationOf(IReadOnlyList<IRaskFile> files);
}
