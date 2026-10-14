using System;
using System.Net.Http;

namespace Rask.TypeScript.Tasks;

/// <summary>The registry answered, and not with success: the status it gave, as a number, for deciding whether to ask again.</summary>
internal sealed class RegistryAnswerException : HttpRequestException
{
    public RegistryAnswerException(int status, string url)
        : base($"{url} answered {status}") => Status = status;

    public RegistryAnswerException()
    {
    }

    public RegistryAnswerException(string message)
        : base(message)
    {
    }

    public RegistryAnswerException(string message, Exception inner)
        : base(message, inner)
    {
    }

    /// <summary>The HTTP status the registry answered with; 0 when none was given.</summary>
    public int Status { get; }
}
