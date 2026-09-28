namespace Rask.Batteries;

/// <summary>A fake battery's expectation that did not hold.</summary>
public sealed class CountingException : Exception
{
    /// <summary>An expectation that did not hold, with nothing more to say.</summary>
    public CountingException()
    {
    }

    /// <summary>An expectation that did not hold, described by <paramref name="message" />.</summary>
    public CountingException(string message) : base(message)
    {
    }

    /// <summary>An expectation that did not hold because of <paramref name="innerException" />.</summary>
    public CountingException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
