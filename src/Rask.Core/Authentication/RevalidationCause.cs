namespace Rask.Core.Authentication;

/// <summary>
///     Whether the question an <see cref="ISessionRevalidator" /> is being asked comes from a reader's action or from
///     nobody's: a render the server started itself.
/// </summary>
/// <remarks>
///     A sign-in that slides — its expiry moves on each use — must be moved by its user, not by a page that repaints
///     in an empty room. The host says which it is here, on the flow of the question, and a revalidator that keeps
///     such an expiry reads it. Internal: Rask.Auth's own revalidator is the one that does.
/// </remarks>
internal static class RevalidationCause
{
    private static readonly AsyncLocal<bool> Unattended = new();

    /// <summary>True while a question is asked that no reader's action started.</summary>
    internal static bool IsUnattended => Unattended.Value;

    /// <summary>Marks what is asked until the result is disposed as nobody's action.</summary>
    internal static Scope EnterUnattended()
    {
        Unattended.Value = true;
        return default;
    }

    /// <summary>Ends <see cref="EnterUnattended" />.</summary>
    internal readonly struct Scope : IDisposable
    {
        /// <inheritdoc />
        public void Dispose() => Unattended.Value = false;
    }
}
