using Microsoft.Extensions.DependencyInjection;
using Rask.Dashboard.Panels;
using Rask.Example.Shop.Features.Ops;

namespace Rask.Example.Shop.Tests;

/// <summary>
/// The dashboard's backup probe has to survive the configuration this app actually ships with.
/// </summary>
/// <remarks>
/// <para>
/// The failure this guards against is nastier than a startup crash: the probe is resolved when the System
/// panel <i>renders</i>, so an app with a missing registration boots perfectly, serves every other page,
/// and then throws the first time an operator opens that one tab — in whichever environment skipped the
/// optional configuration. Found by opening the page, not by any test that existed at the time.
/// </para>
/// <para>
/// <c>AddRaskSqliteLitestream</c> is config-gated on <c>Litestream:ReplicaUrl</c> in this sample and in
/// everything <c>rask new</c> scaffolds, so "no <c>LitestreamStatus</c> in the container" is the normal
/// case, not an edge case.
/// </para>
/// </remarks>
public sealed class ShopBackupProbeTests
{
    [Fact]
    public async Task The_probe_resolves_and_reads_with_no_backup_services_registered()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDashboardBackupProbe, BackupProbe>();

        await using var provider = services.BuildServiceProvider();

        // Resolution is the assertion: a required LitestreamStatus/ISqliteSnapshotStore throws here.
        var probe = provider.GetRequiredService<IDashboardBackupProbe>();

        // null, not a fabricated "stopped" — continuous backup isn't configured, which is not the same
        // as it being broken.
        Assert.Null(await probe.ReplicationAsync(CancellationToken.None));
        Assert.Empty(await probe.SnapshotsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task The_probe_reports_replication_when_litestream_is_registered()
    {
        var services = new ServiceCollection();
        services.AddSingleton<SQLite.Litestream.LitestreamStatus>();
        services.AddSingleton<IDashboardBackupProbe, BackupProbe>();

        await using var provider = services.BuildServiceProvider();
        var probe = provider.GetRequiredService<IDashboardBackupProbe>();

        var replication = await probe.ReplicationAsync(CancellationToken.None);

        Assert.NotNull(replication);
        Assert.False(replication!.IsReplicating);   // registered, but the supervisor hasn't started
        Assert.Equal(0, replication.RestartCount);
    }
}
