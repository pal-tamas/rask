using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.App;

/// <content><c>Db.Transaction</c> on a hand-wired host, from an HTTP endpoint and from a live event handler.</content>
public sealed partial class HandWiredDataHostTests
{
    [Fact]
    public async Task An_endpoint_s_transaction_commits_both_rows_in_the_request_s_tenant_and_a_throw_takes_them_back()
    {
        using var host = await StartAsync();

        var saved = await GetAsync(host, "/api/terminals/pair?name=Gyor", tenant: 8);
        var failed = await GetAsync(host, "/api/terminals/pair?name=Pecs&fail=1", tenant: 8);

        Assert.Equal(("saved", "rolled back"), (saved, failed));
        Assert.Equal((8, 8), (await TenantOfRowAsync(host, "Gyor"), await TenantOfRowAsync(host, "Gyor-2")));
        Assert.Equal(0, await RowsNamedAsync(host, "Pecs"));
    }

    [Fact]
    public async Task A_live_handler_s_transaction_reads_its_own_write_and_commits_both_rows_in_the_session_s_tenant()
    {
        using var host = await StartAsync(TenantFrom.ThePrincipal);
        using var page = await OpenAsync(host, TenantFrom.ThePrincipal);
        var gate = host.Services.GetRequiredService<HandlerGate>();

        await page.Socket.SendJsonAsync(new { id = HandlerOf(page.Html, "pair") }, ct: TestContext.Current.CancellationToken);
        await gate.Reached.Task.WaitAsync(LiveFrames.HangCeiling, TestContext.Current.CancellationToken);

        Assert.True(gate.SawOwnWrite);
        Assert.Equal((7, 7), (await TenantOfRowAsync(host, "Pair-1"), await TenantOfRowAsync(host, "Pair-2")));
    }

    private static async Task<int> RowsNamedAsync(RaskTestHost host, string prefix)
    {
        await using var legacy = await host.Services.GetRequiredService<IDbContextFactory<LegacyTerminalContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);
        return await legacy.Terminals.CountAsync(t => t.Name.StartsWith(prefix), TestContext.Current.CancellationToken);
    }
}
