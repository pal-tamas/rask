using System.Net;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Endpoints;

/// <summary>
///     The UI kit's precompiled sheet, answered by any Rask.Server host at the address <see cref="UiStylesheet.Href" />
///     writes — a hand-wired one too, which links it in its own document (#1315).
/// </summary>
/// <remarks>
///     In the ScopedAssets collection, which runs alone: a path base is a process-wide static read per request.
/// </remarks>
[Collection("ScopedAssets")]
public sealed class KitStylesheetTests
{
    [Fact]
    public async Task A_hand_wired_host_answers_the_kit_sheet_at_its_href()
    {
        using var host = RaskTestHost.Create<QuietApp>();

        using var response = await host.Http.GetAsync(UiStylesheet.Href(), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/css", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(UiStylesheet.Css, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Under_a_path_base_the_kit_sheet_answers_at_the_prefixed_href()
    {
        using var host = RaskTestHost.Create<QuietApp>(pathBase: "/appA");

        using var response = await host.Http.GetAsync(UiStylesheet.Href("/appA"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/css", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Without_its_version_the_kit_sheet_is_revalidated_rather_than_kept()
    {
        using var host = RaskTestHost.Create<QuietApp>();

        using var response = await host.Http.GetAsync(UiStylesheet.Path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-cache", response.Headers.CacheControl?.ToString());
    }
}
