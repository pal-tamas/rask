namespace Rask.Auth;

/// <summary>Reads and writes the single row that records who claimed this instance.</summary>
internal interface IInstanceClaimStore
{
    /// <summary>Whether an account has already claimed this instance.</summary>
    Task<bool> IsClaimedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Claims the instance for <paramref name="userId"/>, returning whether this caller won.
    /// </summary>
    /// <remarks>
    /// <c>false</c> means somebody else claimed it — either earlier, or concurrently. The caller
    /// becomes an ordinary user; it must not retry or treat this as an error.
    /// </remarks>
    Task<bool> TryClaimAsync(Guid userId, CancellationToken cancellationToken = default);
}
