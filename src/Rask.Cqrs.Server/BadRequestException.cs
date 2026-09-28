using Microsoft.AspNetCore.Http;

namespace Rask.Cqrs.Server;

/// <summary>
///     A rejection with the problem document it should become. Thrown from decode so the endpoint can
///     answer with the status the failure actually deserves rather than a blanket 400.
/// </summary>
public sealed class BadRequestException : Exception
{
    /// <summary>Creates a plain 400 with a generic title.</summary>
    public BadRequestException()
        : this("Bad request")
    {
    }

    /// <summary>Creates a 400 titled <paramref name="message" />.</summary>
    /// <param name="message">The problem's title.</param>
    public BadRequestException(string message)
        : this(StatusCodes.Status400BadRequest, message, detail: null)
    {
    }

    /// <summary>Creates a 400 titled <paramref name="message" />, caused by <paramref name="innerException" />.</summary>
    /// <param name="message">The problem's title.</param>
    /// <param name="innerException">The failure that made the request unacceptable.</param>
    public BadRequestException(string message, Exception innerException)
        : base(message, innerException)
    {
        Status = StatusCodes.Status400BadRequest;
        Title = message;
    }

    /// <summary>Creates a rejection answered with <paramref name="status" />.</summary>
    /// <param name="status">The HTTP status to answer with.</param>
    /// <param name="title">The problem's title.</param>
    /// <param name="detail">The problem's detail, or null for none.</param>
    public BadRequestException(int status, string title, string? detail)
        : base(title)
    {
        Status = status;
        Title = title;
        Detail = detail;
    }

    /// <summary>The HTTP status the rejection is answered with.</summary>
    public int Status { get; }

    /// <summary>The problem document's title.</summary>
    public string Title { get; }

    /// <summary>The problem document's detail, if any.</summary>
    public string? Detail { get; }
}
