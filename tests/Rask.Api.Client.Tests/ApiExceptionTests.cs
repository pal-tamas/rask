namespace Rask.Api.Client.Tests;

/// <summary>The standard exception constructors, for code that throws or wraps one without a call behind it.</summary>
public sealed class ApiExceptionTests
{
    [Fact]
    public void An_exception_made_from_a_message_names_no_call_and_no_status()
    {
        var cause = new TimeoutException("slow");

        var error = new ApiException("The call failed.", cause);

        Assert.Equal("The call failed.", error.Message);
        Assert.Same(cause, error.InnerException);
        Assert.Equal(string.Empty, error.Method);
        Assert.Equal(string.Empty, error.Path);
        Assert.Null(error.StatusCode);
    }
}
