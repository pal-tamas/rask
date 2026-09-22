using Rask.Core.Routing;

namespace Rask.Core.Tests.Routing;

public class RouteBindExceptionTests
{
    [Fact]
    public void The_exception_preserves_its_message()
    {
        var ex = new RouteBindException("boom");

        Assert.Equal("boom", ex.Message);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public void The_exception_preserves_its_message_and_inner_exception()
    {
        var inner = new InvalidOperationException("inner");

        var ex = new RouteBindException("boom", inner);

        Assert.Equal("boom", ex.Message);
        Assert.Same(inner, ex.InnerException);
    }
}
