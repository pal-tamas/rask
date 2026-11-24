namespace Rask.Testing;

/// <summary>A <c>Test.FormShows(…)</c> expectation that did not hold. The message says what the form showed instead.</summary>
public sealed class FormAssertionException : Exception
{
    /// <summary>An expectation that did not hold, with nothing more to say.</summary>
    public FormAssertionException()
    {
    }

    /// <summary>An expectation that did not hold, described by <paramref name="message" />.</summary>
    /// <param name="message">What was expected, and what the form showed.</param>
    public FormAssertionException(string message)
        : base(message)
    {
    }

    /// <summary>An expectation that did not hold because of <paramref name="innerException" />.</summary>
    /// <param name="message">What was expected, and what happened.</param>
    /// <param name="innerException">What went wrong on the way.</param>
    public FormAssertionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
