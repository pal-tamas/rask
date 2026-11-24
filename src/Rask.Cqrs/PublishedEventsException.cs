namespace Rask.Cqrs;

/// <summary>An <c>events.Published&lt;T&gt;()</c> expectation that did not hold. The message names what was published instead.</summary>
public sealed class PublishedEventsException : Exception
{
    /// <summary>An expectation that did not hold, with nothing more to say.</summary>
    public PublishedEventsException()
    {
    }

    /// <summary>An expectation that did not hold, described by <paramref name="message" />.</summary>
    /// <param name="message">What was expected, and what was published.</param>
    public PublishedEventsException(string message)
        : base(message)
    {
    }

    /// <summary>An expectation that did not hold because of <paramref name="innerException" />.</summary>
    /// <param name="message">What was expected, and what was published.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public PublishedEventsException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
