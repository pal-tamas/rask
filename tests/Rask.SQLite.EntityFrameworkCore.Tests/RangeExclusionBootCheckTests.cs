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
            o.UseRaskSqlite("Data Source=:memory:", s => s.StrictTables = strictTables));

        await using var provider = services.BuildServiceProvider();

        var hosted = provider.GetServices<IHostedService>().ToList();
        Assert.NotEmpty(hosted);

        foreach (var service in hosted)
        {
            await service.StartAsync(CancellationToken.None);
        }
    }
}
