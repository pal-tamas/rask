using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rask.Data;

namespace Rask.SQLite.EntityFrameworkCore.Tests;

// AddRaskData<TContext> refuses to boot a context whose provider would ignore HasNonOverlappingRange. That check
// is only worth having if the provider that DOES enforce the rule passes it — through both generators
// UseRaskSqlite can register, since the strict one is a separate type.
public sealed class RangeExclusionBootCheckTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UseRaskSqlite_satisfies_the_boot_check(bool strictTables)
    {
        var services = new ServiceCollection();
        services.AddRaskData<BookingContext>();
        services.AddDbContextFactory<BookingContext>(o =>
            o.UseRaskSqliteAt("Data Source=:memory:", s => s.StrictTables = strictTables));

        await using var provider = services.BuildServiceProvider();

        var hosted = provider.GetServices<IHostedService>().ToList();
        Assert.NotEmpty(hosted);

        foreach (var service in hosted)
        {
            await service.StartAsync(CancellationToken.None);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UseRaskSqlite_satisfies_the_full_text_search_boot_check(bool strictTables)
    {
        var services = new ServiceCollection();
        services.AddRaskData<ArticleContext>();
        services.AddDbContextFactory<ArticleContext>(o =>
            o.UseRaskSqliteAt("Data Source=:memory:", s => s.StrictTables = strictTables));

        await using var provider = services.BuildServiceProvider();

        foreach (var service in provider.GetServices<IHostedService>())
        {
            await service.StartAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task A_plain_UseSqlite_fails_the_full_text_search_boot_check()
    {
        var services = new ServiceCollection();
        services.AddRaskData<ArticleContext>();
        services.AddDbContextFactory<ArticleContext>(o => o.UseSqlite("Data Source=:memory:"));

        await using var provider = services.BuildServiceProvider();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            foreach (var service in provider.GetServices<IHostedService>())
            {
                await service.StartAsync(CancellationToken.None);
            }
        });

        Assert.StartsWith("Article declares HasFullTextSearch, but Microsoft.EntityFrameworkCore.Sqlite does not support it", error.Message);
        Assert.Contains("Full-text search is SQLite-only for now: configure ArticleContext with UseRaskSqlite(services)", error.Message);
    }
}
