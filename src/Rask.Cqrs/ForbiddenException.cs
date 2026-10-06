namespace Rask.Cqrs;

/// <summary>
///     A query or command was refused before its handler ran, because the handler's <c>[Authorize]</c> does not
///     admit whoever sent it.
/// </summary>
/// <remarks>
///     One type for both refusals; <see cref="IsAuthenticated" /> says which. The server endpoint answers it as
///     a 401 or a 403, and in-process it reaches the caller as itself.
/// </remarks>
public sealed class ForbiddenException : Exception
{
    /// <summary>Creates the exception with no detail.</summary>
    public ForbiddenException()
        : this("The request is not permitted.")
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">What was refused, and what it requires.</param>
    public ForbiddenException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and the exception that caused it.</summary>
    /// <param name="message">What was refused, and what it requires.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public ForbiddenException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates the exception, saying whether anyone was signed in.</summary>
    /// <param name="message">What was refused, and what it requires.</param>
    /// <param name="isAuthenticated">Whether the caller was signed in.</param>
    public ForbiddenException(string message, bool isAuthenticated)
        : base(message)
    {
        IsAuthenticated = isAuthenticated;
    }

    /// <summary>
    ///     True when the caller was signed in and is not permitted; false when nobody was signed in.
    /// </summary>
    public bool IsAuthenticated { get; }
}
