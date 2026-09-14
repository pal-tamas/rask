using System.Collections.Concurrent;
using System.Security.Claims;
using Rask.Core.Diagnostics;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Authentication;

// The ownership rule behind a hello, an upload and a download. #1102: two signed-in principals that carry neither a
// NameIdentifier nor a Name claim compared null with null and matched — any such user owned any other's session.
[Collection("DiagnosticsSink")]
public sealed class SameSessionUserTests
{
    private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());

    private static ClaimsPrincipal SignedIn(params Claim[] claims) => new(new ClaimsIdentity(claims, "TestCookie"));

    [Fact]
    public void An_anonymous_session_is_matched_by_anyone() =>
        Assert.True(RaskEndpointExtensions.SameSessionUser(SignedIn(new Claim(ClaimTypes.Name, "mallory")), Anonymous()));

    [Fact]
    public void The_same_identifier_matches_and_a_different_one_does_not()
    {
        var alice = SignedIn(new Claim(ClaimTypes.NameIdentifier, "alice"));

        Assert.True(RaskEndpointExtensions.SameSessionUser(SignedIn(new Claim(ClaimTypes.NameIdentifier, "alice")), alice));
        Assert.False(RaskEndpointExtensions.SameSessionUser(SignedIn(new Claim(ClaimTypes.NameIdentifier, "bob")), alice));
        Assert.False(RaskEndpointExtensions.SameSessionUser(Anonymous(), alice));
    }

    [Fact]
    public void Two_keyless_signed_in_users_do_not_match_and_it_is_said_once()
    {
        var captured = new ConcurrentQueue<RaskDiagnosticEvent>();
        var previous = RaskDiagnostics.Sink;
        RaskEndpointExtensions.ResetKeylessPrincipalReportForTests();
        RaskDiagnostics.Sink = captured.Enqueue;
        try
        {
            var keyless = SignedIn(new Claim(ClaimTypes.Email, "a@example.test"));
            var otherKeyless = SignedIn(new Claim(ClaimTypes.Email, "b@example.test"));

            Assert.False(RaskEndpointExtensions.SameSessionUser(otherKeyless, keyless));
            Assert.False(RaskEndpointExtensions.SameSessionUser(keyless, keyless));
            // A keyed request cannot claim a keyless session either.
            Assert.False(RaskEndpointExtensions.SameSessionUser(SignedIn(new Claim(ClaimTypes.Name, "alice")), keyless));

            var warning = Assert.Single(captured, e => e.Message.Contains("NameIdentifier", StringComparison.Ordinal));
            Assert.Equal(RaskLogLevel.Warning, warning.Level);
        }
        finally
        {
            RaskDiagnostics.Sink = previous;
        }
    }
}
