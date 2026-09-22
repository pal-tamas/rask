using Rask.Core.Authentication;

namespace Rask.Core.Tests.Authentication;

public class AnonymousUserProviderTests
{
    [Fact]
    public void The_current_principal_is_unauthenticated()
    {
        var provider = new AnonymousUserProvider();

        Assert.NotNull(provider.Current.Identity);
        Assert.False(provider.Current.Identity!.IsAuthenticated);
    }

    [Fact]
    public void Subscribing_and_unsubscribing_Changed_does_not_throw()
    {
        var provider = new AnonymousUserProvider();
        var handler = () => { };

        provider.Changed += handler;
        provider.Changed -= handler;
    }
}
