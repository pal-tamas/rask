using Rask.Core.Authentication;

namespace Rask.Core.Tests.Authentication;

public class AnonymousUserProviderTests
{
    [Fact]
    public void Current_ReturnsUnauthenticatedPrincipal()
    {
        var provider = new AnonymousUserProvider();
        Assert.NotNull(provider.Current.Identity);
        Assert.False(provider.Current.Identity!.IsAuthenticated);
    }

    [Fact]
    public void Changed_DoesNotThrowWhenSubscribedAndUnsubscribed()
    {
        var provider = new AnonymousUserProvider();
        Action handler = () => { };
        provider.Changed += handler;
        provider.Changed -= handler;
    }
}
