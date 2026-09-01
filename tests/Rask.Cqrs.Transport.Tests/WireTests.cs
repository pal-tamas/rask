using System.Net;

namespace Rask.Cqrs.Transport.Tests;

/// <summary>
///     The two halves of remote dispatch, put in front of each other. Every case starts at a client
///     dispatch and ends at a decoded answer, so a route, a verb, a header or a status the two sides
///     disagreed about fails here rather than in someone's app.
/// </summary>
/// <remarks>
///     Both halves were previously tested only against a stand-in for the other — the client against a
///     fake <c>HttpMessageHandler</c>, the server against hand-built <c>HttpRequestMessage</c>s — so each
///     could be self-consistently wrong and both suites stay green (#896).
/// </remarks>
public sealed class WireTests
{
    [Fact]
    public async Task A_query_goes_out_as_a_GET_and_comes_back_decoded()
    {
        await using var wire = Wire.Connect();

        var greeting = await wire.SendAsync<Greeting>(new GetGreeting("Ada", Formal: true));

        Assert.Equal("Good day, Ada.", greeting.Text);
        Assert.Equal(14, greeting.Length);
        Assert.True(greeting.Formal);

        Assert.Equal(HttpMethod.Get, wire.Recorder.Last.Method);
        WireAssert.Path(wire.Recorder.Last.Uri, "Rask.Cqrs.Transport.Tests.GetGreeting");
    }

    [Fact]
    public async Task A_query_too_long_for_a_url_falls_back_to_POST_and_answers_identically()
    {
        // The client decides this on its own, above MaxQueryUrlLength. The server has no idea a fallback
        // happened — it just has to accept the same message in a body instead of a query string, which is
        // a different parse on its side. A short and a long query must be indistinguishable in the answer.
        await using var wire = Wire.Connect();

        var padding = new string('x', 4000);
        var counted = await wire.SendAsync<int>(new CountCharacters(padding));

        Assert.Equal(4000, counted);
        Assert.Equal(HttpMethod.Post, wire.Recorder.Last.Method);
        Assert.Equal("application/json", wire.Recorder.Last.ContentType);
    }

    [Fact]
    public async Task A_command_that_returns_a_value_travels_as_a_POST_and_lands_on_the_handler()
    {
        await using var wire = Wire.Connect();

        Assert.Equal(3, await wire.SendAsync<int>(new Bump(3)));
        Assert.Equal(5, await wire.SendAsync<int>(new Bump(2)));

        Assert.Equal(5, wire.Ledger.Count);
        Assert.Equal(HttpMethod.Post, wire.Recorder.Last.Method);
    }

    [Fact]
    public async Task A_void_command_is_answered_204_and_still_ran()
    {
        await using var wire = Wire.Connect();

        await wire.SendAsync(new Touch("kettle"));

        Assert.Contains("touched:kettle", wire.Ledger.Entries);
    }

    [Fact]
    public async Task A_notification_is_accepted_rather_than_answered()
    {
        // 202, not 204: a notification is fanned out, so "accepted" is the honest word for what the
        // server did with it. The client must be happy with either — it reads no body.
        await using var wire = Wire.Connect();

        await wire.PublishAsync(new Announce("deployed"));

        Assert.Contains("announced:deployed", wire.Ledger.Entries);
    }

    [Fact]
    public async Task Every_request_carries_the_header_the_endpoint_refuses_to_work_without()
    {
        // The CSRF control, from both ends at once: the client sets it on every request and the server
        // rejects anything without it. Neither suite alone can show the two agree on the spelling.
        await using var wire = Wire.Connect();
        await wire.SendAsync<Greeting>(new GetGreeting("Ada", Formal: false));

        var uri = wire.Recorder.Last.Uri;
        using var bare = new HttpRequestMessage(HttpMethod.Get, uri);
        bare.Headers.TryAddWithoutValidation("X-Test-User", "tester");
        using var refused = await wire.Http.SendAsync(bare);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    [Fact]
    public async Task A_message_the_server_has_no_handler_for_names_itself_in_the_failure()
    {
        await using var wire = Wire.Connect();

        var error = await Assert.ThrowsAsync<RemoteDispatchException>(
            () => wire.SendAsync<int>(new Unhandled()));

        Assert.Equal((int)HttpStatusCode.NotFound, error.StatusCode);
        Assert.Equal("Rask.Cqrs.Transport.Tests.Unhandled", error.MessageName);
    }

    [Fact]
    public async Task An_anonymous_caller_is_refused_before_the_handler()
    {
        await using var wire = Wire.Connect(user: null);

        var error = await Assert.ThrowsAsync<RemoteDispatchException>(
            () => wire.SendAsync<Greeting>(new GetGreeting("Ada", Formal: false)));

        Assert.Equal((int)HttpStatusCode.Unauthorized, error.StatusCode);
    }

    [Fact]
    public async Task A_role_the_caller_does_not_hold_is_a_403_the_client_reports_as_one()
    {
        await using var wire = Wire.Connect(roles: "reader");

        var error = await Assert.ThrowsAsync<RemoteDispatchException>(() => wire.SendAsync(new Purge()));

        Assert.Equal((int)HttpStatusCode.Forbidden, error.StatusCode);
        Assert.DoesNotContain("purged", wire.Ledger.Entries);
    }

    [Fact]
    public async Task The_role_the_handler_declared_is_the_one_that_lets_it_through()
    {
        await using var wire = Wire.Connect(roles: "admin");

        await wire.SendAsync(new Purge());

        Assert.Contains("purged", wire.Ledger.Entries);
    }

    [Fact]
    public async Task A_handler_that_throws_reaches_the_client_as_a_500_with_nothing_leaked()
    {
        // Opaque by default. The exception message here names a connection string, and the point of the
        // default is that it stays on the server — a test on one side alone cannot show what the other
        // side actually received.
        await using var wire = Wire.Connect();

        var error = await Assert.ThrowsAsync<RemoteDispatchException>(
            () => wire.SendAsync<int>(new Explodes()));

        Assert.Equal((int)HttpStatusCode.InternalServerError, error.StatusCode);
        Assert.DoesNotContain("hunter2", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", error.Detail ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Turning_on_exception_detail_is_what_puts_the_cause_on_the_wire()
    {
        // The inverse, so the previous case is pinned as a choice rather than an accident of plumbing.
        await using var wire = Wire.Connect(configureServer: o => o.IncludeExceptionDetail = true);

        var error = await Assert.ThrowsAsync<RemoteDispatchException>(
            () => wire.SendAsync<int>(new Explodes()));

        Assert.Contains("hunter2", error.Detail ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_command_cannot_be_reached_by_the_query_verb()
    {
        // The client never sends a command as a GET. This is the server holding the same line from the
        // other side, which is what makes the rule a property of the wire rather than of one half.
        await using var wire = Wire.Connect();

        var url = RemoteEndpointDefaults.RoutePrefix + "/Rask.Cqrs.Transport.Tests.Bump?"
                  + RemoteEndpointDefaults.MessageQueryParameter + "=" + Uri.EscapeDataString("""{"by":4}""");

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation(
            RemoteEndpointDefaults.RequestHeader, RemoteEndpointDefaults.RequestHeaderValue);
        request.Headers.TryAddWithoutValidation("X-Test-User", "tester");

        using var response = await wire.Http.SendAsync(request);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal(0, wire.Ledger.Count);
    }

    [Fact]
    public async Task A_route_prefix_moved_on_one_side_only_stops_working()
    {
        // RoutePrefix is configurable on both halves, and the two settings are independent. This is the
        // negative that gives the positive cases their meaning: the round trips above pass because the
        // prefixes agree, not because the path is ignored.
        await using var wire = Wire.Connect(configureClient: o => o.RoutePrefix = "/elsewhere/cqrs");

        var error = await Assert.ThrowsAsync<RemoteDispatchException>(
            () => wire.SendAsync<Greeting>(new GetGreeting("Ada", Formal: false)));

        Assert.Equal((int)HttpStatusCode.NotFound, error.StatusCode);
    }

    [Fact]
    public async Task Moving_the_prefix_on_both_sides_together_keeps_working()
    {
        await using var wire = Wire.Connect(
            configureServer: o => o.RoutePrefix = "/elsewhere/cqrs",
            configureClient: o => o.RoutePrefix = "/elsewhere/cqrs");

        var greeting = await wire.SendAsync<Greeting>(new GetGreeting("Ada", Formal: false));

        Assert.Equal("hi Ada", greeting.Text);
        Assert.StartsWith("/elsewhere/cqrs/", wire.Recorder.Last.Uri.AbsolutePath, StringComparison.Ordinal);
    }
}
