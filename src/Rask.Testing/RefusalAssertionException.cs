namespace Rask.Testing;

/// <summary>A <c>Test.Refuses(…)</c> expectation that did not hold. The message says what happened instead.</summary>
public sealed class RefusalAssertionException : Exception
{
    /// <summary>An expectation that did not hold, with nothing more to say.</summary>
    public RefusalAssertionException()
    {
    }

    /// <summary>An expectation that did not hold, described by <paramref name="message" />.</summary>
    /// <param name="message">What was expected, and what happened.</param>
    public RefusalAssertionException(string message)
        : base(message)
    {
    }

    /// <summary>An expectation that did not hold because of <paramref name="innerException" />.</summary>
    /// <param name="message">What was expected, and what happened.</param>
    /// <param name="innerException">What the operation threw instead of a refusal.</param>
    public RefusalAssertionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
