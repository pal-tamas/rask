using Microsoft.Extensions.DependencyInjection;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Authentication;

/// <content>The same, for an app mounted under a path base.</content>
public sealed partial class SessionStaysItsUsersTests
{
    /// <summary>
    ///     A path base is the process's (<c>LiveOptions.PathBase</c>), so a host mounted under one changes the runtime
    ///     tag of every page rendered beside it. These run alone, in the collection of the other path-base tests.
    /// </summary>
    [Collection("ScopedAssets")]
    public sealed class UnderAPathBase
    {
        // A host that signs in only the requests under its path base: the socket is one of them, because the path
        // base is part of its route and not moved into Request.PathBase.
        [Fact]
        public async Task The_socket_is_a_request_under_it_and_carries_the_user_as_the_page_does()
        {
            using var host = CreateHost(pathBase: "/uj");
            var (sessionId, save) = await OpenPageAsync(host, "alice", "/uj");
            host.Services.GetRequiredService<Directory>().Admins.Clear();

            using var ws = await AttachAsync(host, sessionId, "alice");
            await ClickAsync(host, ws, save);

            Assert.Equal(new Save("alice:user", "alice:admin", "alice"), Saves(host).Single());
        }

        [Fact]
        public async Task The_mark_names_that_app_and_is_removed_once_used()
        {
            using var host = CreateHost<FollowApp>(pathBase: "/uj");
            using var request = new HttpRequestMessage(HttpMethod.Get, "/uj/open");
            request.Headers.Add("Cookie", "rask-ended=%2Fuj");

            using var response = await host.Http.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal("/uj/login?returnUrl=%2Fopen", response.Headers.Location?.OriginalString);
            Assert.Contains(response.Headers.GetValues("Set-Cookie"), static c => c.StartsWith("rask-ended=;", StringComparison.Ordinal));
        }
    }
}
