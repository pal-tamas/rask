using Rask.Core.Authorization;
using Rask.Core.Routing;

namespace Rask.Core.Tests.Routing;

// The way back a sign-in challenge carries (?returnUrl=): escaped once, never wrapped again when the page challenged
// is the sign-in page itself, and the root past LocalUrl.MaxLength — so a challenge loop cannot grow into a 431.
public class ChallengeReturnUrlTests
{
    [Fact]
    public void A_challenge_carries_the_page_and_its_query_escaped_once()
    {
        const string page = "/orders?tab=2";

        var query = ChallengeReturnUrl.Query(page);

        Assert.Equal("?returnUrl=%2Forders%3Ftab%3D2", query);
    }

    [Fact]
    public void A_sign_in_page_challenged_round_and_round_keeps_the_same_address()
    {
        var first = RouteAuthorizationGuard.ChallengePath + ChallengeReturnUrl.Query("/orders?tab=2");

        var again = first;
        for (var i = 0; i < 10; i++)
        {
            again = RouteAuthorizationGuard.ChallengePath + ChallengeReturnUrl.Query(again);
        }

        Assert.Equal(first, again);
    }

    [Fact]
    public void A_sign_in_page_with_no_way_back_sends_the_reader_back_to_the_root()
    {
        const string page = "/login";

        var wayBack = ChallengeReturnUrl.For(page);

        Assert.Equal("/", wayBack);
    }

    [Fact]
    public void A_sign_in_page_in_another_language_is_recognised_through_the_route_path()
    {
        static string RoutePathOf(string path) => path.StartsWith("/de/", StringComparison.Ordinal) ? path[3..] : path;

        var wayBack = ChallengeReturnUrl.For("/de/login?returnUrl=%2Fde%2Fkonto", RoutePathOf);

        Assert.Equal("/de/konto", wayBack);
    }

    [Fact]
    public void An_off_site_way_back_inside_a_sign_in_page_collapses_to_the_root()
    {
        const string page = "/login?returnUrl=%2F%2Fevil.com";

        var wayBack = ChallengeReturnUrl.For(page);

        Assert.Equal("/", wayBack);
    }

    [Fact]
    public void A_page_past_the_cap_is_sent_back_to_the_root()
    {
        var page = "/orders?q=" + new string('a', LocalUrl.MaxLength);

        var query = ChallengeReturnUrl.Query(page);

        Assert.Equal("?returnUrl=%2F", query);
    }
}
