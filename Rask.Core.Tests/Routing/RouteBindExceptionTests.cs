using Rask.Core.Routing;

namespace Rask.Core.Tests.Routing;

public class RouteBindExceptionTests
{
    [Fact]
    public void Constructor_PreservesMessage()
    {
        var ex = new RouteBindException("boom");

        Assert.Equal("boom", ex.Message);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public void Constructor_PreservesMessageAndInnerException()
    {
        var inner = new InvalidOperationException("inner");

        var ex = new RouteBindException("boom", inner);

        Assert.Equal("boom", ex.Message);
        Assert.Same(inner, ex.InnerException);
    }
}
