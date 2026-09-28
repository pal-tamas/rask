namespace Rask.Cqrs.Server;

/// <summary>
///     A chunk arrived at an offset the server is not at. Carries the offset it IS at, so the client can
///     continue from there rather than start again.
/// </summary>
public sealed class UploadOffsetException : Exception
{
    /// <summary>Creates the exception with a generic message and no known offset.</summary>
    public UploadOffsetException()
        : this("The chunk does not follow on from what the server holds.")
    {
    }

    /// <summary>Creates the exception with <paramref name="message" /> and no known offset.</summary>
    /// <param name="message">What went wrong.</param>
    public UploadOffsetException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with <paramref name="message" />, caused by <paramref name="innerException" />.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The failure behind it.</param>
    public UploadOffsetException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates the exception for a server that holds <paramref name="expected" /> bytes.</summary>
    /// <param name="expected">The number of bytes the server holds for the file.</param>
    public UploadOffsetException(long expected)
        : base($"The server holds {expected} bytes for this file.")
    {
        Expected = expected;
    }

    /// <summary>The number of bytes the server holds, which is where the next chunk must start.</summary>
    public long Expected { get; }
}
