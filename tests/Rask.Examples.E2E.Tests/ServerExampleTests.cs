using Rask.Examples.E2E.Tests.Infrastructure;

namespace Rask.Examples.E2E.Tests;

// The Server host's single browser journey. ASP.NET host with a SPA fallback and a live
// WebSocket, so it runs every step: deep-link + refresh, slow-3G throttling, and the
// offline→online WebSocket reconnect that preserves server-held session state.
[Collection(ServerExampleCollection.Name)]
public sealed class ServerExampleTests(ServerExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    protected override string BaseUrl => app.BaseUrl;
    protected override string FixtureName => "Server";
    protected override string ServerLog => app.ServerLog;

    [Fact]
    public Task Journey_WalksEveryPageAndUnusualActivity() => RunAsync(() =>
        RunShowcaseJourneyAsync(new ShowcaseJourneyOptions
        {
            DeepLink = true,
            OfflineReconnect = true,
            Slow3g = true,
        }));
}
