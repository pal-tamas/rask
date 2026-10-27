namespace Rask.Core.Live;

/// <summary>What an app's callback threw after the token it was handed was cancelled.</summary>
/// <remarks>
///     A callback need not report its cancellation as an <see cref="OperationCanceledException" />: EF's SQL Server
///     retrying strategy turns a cancelled query into an <see cref="InvalidOperationException" /> (#1256). Whatever it
///     throws once its token is cancelled is that cancellation — the client went away — and is made one, so every
///     caller ends it as quietly as any other.
/// </remarks>
internal static class CancelledCallback
{
    /// <summary>Whether <paramref name="ex" /> is a cancellation of <paramref name="ct" /> that came out as something else.</summary>
    public static bool IsDisguised(Exception ex, CancellationToken ct) =>
        ct.IsCancellationRequested && ex is not OperationCanceledException;

    /// <summary>The cancellation <paramref name="ex" /> stands for, carrying it as the inner exception.</summary>
    public static OperationCanceledException AsCancellation(Exception ex, CancellationToken ct) =>
        new("The operation was canceled.", ex, ct);
}
