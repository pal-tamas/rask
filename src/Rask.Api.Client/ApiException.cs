namespace Rask.Api.Client;

/// <summary>
///     An API call that did not answer with success.
/// </summary>
/// <remarks>
///     <see cref="StatusCode" /> being <see langword="null" /> is the load-bearing distinction: it means
///     the request never reached the server at all — DNS, TLS, a dropped connection, a timeout — and the
///     cause is in <see cref="Exception.InnerException" />. A status means the server answered and said
///     no. The two need different handling at a call site, and an exception type that blurs them makes
///     "is it down or am I wrong?" unanswerable.
/// </remarks>
public sealed class ApiException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="method">The HTTP method that was attempted.</param>
    /// <param name="path">The path that was requested.</param>
    /// <param name="statusCode">The status answered, or null when the request never arrived.</param>
    /// <param name="problemType">The <c>type</c> of an RFC 9457 problem document, when there was one.</param>
    /// <param name="detail">The <c>detail</c> of an RFC 9457 problem document, when there was one.</param>
    /// <param name="innerException">The transport failure, when there was one.</param>
    public ApiException(
        string message,
        string method,
        string path,
        int? statusCode = null,
        string? problemType = null,
        string? detail = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Method = method;
        Path = path;
        StatusCode = statusCode;
        ProblemType = problemType;
        Detail = detail;
    }

    /// <summary>The HTTP method that was attempted.</summary>
    public string Method { get; }

    /// <summary>The path that was requested.</summary>
    public string Path { get; }

    /// <summary>The status the server answered, or null when the request never reached it.</summary>
    public int? StatusCode { get; }

    /// <summary>The <c>type</c> of an RFC 9457 problem document, when the answer carried one.</summary>
    public string? ProblemType { get; }

    /// <summary>The <c>detail</c> of an RFC 9457 problem document, when the answer carried one.</summary>
    public string? Detail { get; }
}
