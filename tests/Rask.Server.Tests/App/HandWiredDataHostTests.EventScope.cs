using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rask.Data;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.App;

public sealed partial class HandWiredDataHostTests
{
    // Each event of one live page works as a request would: its own DbContext, the principal the session revalidated
    // for it, the tenant that principal names, and a write stamped with that tenant.
    [Fact]
    public async Task An_event_after_the_user_s_rights_changed_gets_a_new_context_with_its_user_and_tenant()
    {
        using var host = await StartAsync(TenantFrom.ThePrincipal);
        var chosen = host.Services.GetRequiredService<ChosenPartner>();
        chosen.Kind = TenantFrom.ThePrincipal;
        var opened = await GetAsync(host, "/start", tenant: null, user: Alice, partner: 7);
        using var ws = await AttachAsync(host, MarkupAssert.SessionId(opened), partner: 7);
        var log = host.Services.GetRequiredService<EventWitnessLog>();

        await WitnessAsync(ws, opened, log, 1);
        chosen.Now = 8;
        await WitnessAsync(ws, opened, log, 2);

        Assert.NotSame(log.Seen[0].Context, log.Seen[1].Context);
        Assert.True(log.Seen[0].ContextDisposed());
        Assert.Equal(["7", "8"], log.Seen.Select(static s => s.Partner));
        Assert.Equal(
            [new Guid("00000000-0000-0000-0000-000000000007"), new Guid("00000000-0000-0000-0000-000000000008")],
            log.Seen.Select(static s => s.Tenant));
        await using var legacy = await host.Services.GetRequiredService<IDbContextFactory<LegacyTerminalContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);
        var written = await legacy.Terminals.Where(static t => t.Name.StartsWith("Witness-"))
            .OrderBy(static t => t.Name).Select(static t => t.TenantId).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal([7, 8], written);
    }

    private static async Task WitnessAsync(System.Net.WebSockets.WebSocket ws, string opened, EventWitnessLog log, int count)
    {
        var id = Regex.Match(opened, "<button[^>]*data-rask-on-click=\"(h\\d+)\"[^>]*>witness").Groups[1].Value;
        await ws.SendJsonAsync(new { id }, ct: TestContext.Current.CancellationToken);
        await WaitFor.True(() => log.Seen.Count == count && log.Written >= count, "the witness has written");
        await ws.SettledAsync();
    }
}

/// <summary>What each event's <see cref="EventWitness" /> saw.</summary>
public sealed class EventWitnessLog
{
    private int _written;

    public List<(LegacyTerminalContext Context, string? Partner, Guid? Tenant, Func<bool> ContextDisposed)> Seen { get; } = [];

    public int Written => Volatile.Read(ref _written);

    public void Wrote() => Interlocked.Increment(ref _written);
}

/// <summary>A scoped service of the app's, over a scoped DbContext of its own.</summary>
public sealed class EventWitness(LegacyTerminalContext db, EventWitnessLog log)
{
    public int See()
    {
        log.Seen.Add((db, Current.Principal?.FindFirst("partner")?.Value, Current.Tenant, () => Disposed(db)));
        log.Wrote();
        return log.Seen.Count;
    }

    private static bool Disposed(DbContext db)
    {
        try
        {
            _ = db.Model;
            return false;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
    }
}
