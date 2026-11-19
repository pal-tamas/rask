using System.Net;

namespace Rask.Auth.Tests;

/// <summary>Which client an attempt is counted against.</summary>
public sealed class AuthThrottleClientTests
{
    [Fact]
    public void Two_addresses_of_one_ipv6_subscriber_count_as_one_client()
    {
        var first = AuthThrottle.ClientOf(IPAddress.Parse("2001:db8:1:2:aaaa::1"));
        var second = AuthThrottle.ClientOf(IPAddress.Parse("2001:db8:1:2:ffff:ffff:ffff:ffff"));

        Assert.Equal("2001:db8:1:2::/64", first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void An_ipv4_address_counts_as_itself_mapped_or_not()
    {
        var plain = AuthThrottle.ClientOf(IPAddress.Parse("203.0.113.7"));
        var mapped = AuthThrottle.ClientOf(IPAddress.Parse("::ffff:203.0.113.7"));

        Assert.Equal("203.0.113.7", plain);
        Assert.Equal(plain, mapped);
    }
}
