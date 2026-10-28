using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Server.Tests.App;

/// <summary>
/// A RaskApp's context writes its migrations through Rask's differ — so a collection of value objects turned NOT NULL
/// is filled with [] — registered by an extension the context carries anyway, not by a replaced service.
/// </summary>
public sealed class RaskAppMigrationsDifferTests
{
    [Fact]
    public void A_rask_apps_context_writes_its_migrations_through_rasks_differ()
    {
        var built = RaskApp.Create([], b => b.WebHost.UseSetting("urls", "http://127.0.0.1:0")).Build<MinimalApp>();
        using var db = built.Services.GetRequiredService<IDbContextFactory<RaskAppDbContext>>().CreateDbContext();

        var differ = db.GetService<IMigrationsModelDiffer>();
        var replaced = db.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.ReplacedServices?.Keys;

        Assert.Equal("RaskMigrationsModelDiffer", differ.GetType().Name);
        Assert.DoesNotContain(replaced ?? [], k => k.Item1 == typeof(IMigrationsModelDiffer));
    }
}
