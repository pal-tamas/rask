using System.Security.Claims;

namespace Rask.Core.Authentication;

public interface IUserProvider
{
    ClaimsPrincipal Current { get; }
    event Action? Changed;

    /// <summary>
    ///     Optional one-shot initialization (e.g. fetch /api/me on WASM). Hosts await this before the
    ///     first render to avoid an anonymous-then-authenticated flicker. Default is a no-op.
    /// </summary>
    Task EnsureLoadedAsync() => Task.CompletedTask;

    /// <summary>
    ///     Re-fetch the current user from the source of truth (e.g. /api/me on WASM). Called by the
    ///     framework after sign-out so the UI can transition to anonymous without a full page reload.
    ///     Default is a no-op.
    /// </summary>
    Task RefreshAsync() => Task.CompletedTask;
}
