using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Rask.SQLite.Litestream.Tests;

public sealed class LitestreamServiceCollectionExtensionsTests
{
    [Fact]
    public void AddRaskSqliteLitestream_registers_restorer_executor_and_hosted_service()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRaskSqliteLitestream(o =>
        {
            o.DatabasePath = "/data/app.db";
            o.ReplicaUrl = "s3://bucket/app";
        });

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<LitestreamRestorer>());
        Assert.NotNull(provider.GetService<ILitestreamExecutor>());
        Assert.Single(services, d => d.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public void AddRaskSqliteLitestream_is_idempotent()
    {
        var services = new ServiceCollection();
        services.AddRaskSqliteLitestream(o => { o.DatabasePath = "/a.db"; o.ReplicaUrl = "s3://b/a"; });
        services.AddRaskSqliteLitestream(o => { o.DatabasePath = "/c.db"; o.ReplicaUrl = "s3://d/c"; });

        Assert.Single(services, d => d.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public void AddRaskSqliteLitestream_validates_options()
    {
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() =>
            services.AddRaskSqliteLitestream(o => o.DatabasePath = "/data/app.db")); // no replica / config
    }

    [Fact]
    public async Task RestoreSqliteFromLitestreamAsync_throws_when_not_registered()
    {
        var services = new ServiceCollection();
        using var provider = services.BuildServiceProvider();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.RestoreSqliteFromLitestreamAsync());
    }
}
