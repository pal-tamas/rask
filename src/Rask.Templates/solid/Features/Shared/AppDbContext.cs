using Microsoft.EntityFrameworkCore;
using Rask.Data;
// rask:if outbox
using Rask.Outbox;
// rask:end
// rask:if jobs
using Rask.Jobs;
// rask:end
// rask:if mail
using Rask.Mail;
// rask:end
// rask:if cache
using Rask.Cache;
// rask:end
// rask:if storage
using Rask.Storage;
// rask:end
using Rask.Auth;

namespace Company.RaskServer.Features.Shared;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : RaskDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // RaskDbContext, not DbContext: the base maps every class deriving from Model<TId>, which
        // is what lets you declare an entity and nothing else — no DbSet property, no
        // IEntityTypeConfiguration, no registration. It also brings the value converters for
        // strongly-typed ids, which EF reads before the model is built. Over plain DbContext this
        // file still compiles and every model you declare is silently absent from the database.
        base.OnModelCreating(modelBuilder);

        // ApplyRaskConventions walks the model as it stands, giving each marked entity its audit
        // stamps, its soft-delete query filter and its concurrency token — so it has to come LAST,
        // after the models, the configurations AND every battery's tables. Anything mapped after
        // it silently misses out, which is what a User declaring ITimestamped used to do.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        // rask:if outbox
        modelBuilder.AddRaskOutbox();
        // rask:end
        // rask:if jobs
        modelBuilder.AddRaskJobs();
        // rask:end
        // rask:if mail
        modelBuilder.AddRaskMail();
        // rask:end
        // rask:if cache
        modelBuilder.AddRaskCache();
        // rask:end
        // rask:if storage
        modelBuilder.AddRaskStorage();
        // rask:end
        modelBuilder.AddRaskAuth();
        modelBuilder.ApplyRaskConventions();
    }
}
