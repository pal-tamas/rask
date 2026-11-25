using Microsoft.EntityFrameworkCore;
using Rask.Data;

namespace Rask.Background.Tests;

/// <summary>
/// The job tables are aggregates, and a context that maps them with <c>AddRaskJobs()</c> alone — no
/// <c>ApplyRaskConventions</c> — still gets exactly the columns they always had: no <c>Version</c>, no
/// <c>DeletedAt</c>.
/// </summary>
public sealed class JobTableShapeTests
{
    [Theory]
    [InlineData(typeof(Job))]
    [InlineData(typeof(RecurringJobState))]
    public void A_job_table_mapped_without_the_conventions_has_no_version_and_no_deleted_at(Type table)
    {
        using var db = new JobsDbContext(new DbContextOptionsBuilder<JobsDbContext>().UseSqlite("Data Source=:memory:").Options);

        var entity = db.Model.FindEntityType(table)!;

        Assert.Null(entity.FindProperty(Columns.Version));
        Assert.Null(entity.FindProperty(Columns.DeletedAt));
    }
    [Theory]
    [InlineData(nameof(Job.Payload))]
    [InlineData(nameof(Job.Error))]
    [InlineData(nameof(Job.UserClaims))]
    [InlineData(nameof(Job.Result))]
    public void A_jobs_text_stays_unbounded_under_a_plain_context_that_bounds_every_string(string column)
    {
        using var db = new BoundedStringsContext(new DbContextOptionsBuilder<BoundedStringsContext>().UseSqlite("Data Source=:memory:").Options);

        var property = db.Model.FindEntityType(typeof(Job))!.FindProperty(column)!;

        Assert.Null(property.GetMaxLength());
    }

    [Fact]
    public void A_jobs_type_keeps_its_own_bound_under_a_plain_context_that_bounds_every_string()
    {
        using var db = new BoundedStringsContext(new DbContextOptionsBuilder<BoundedStringsContext>().UseSqlite("Data Source=:memory:").Options);

        var property = db.Model.FindEntityType(typeof(Job))!.FindProperty(nameof(Job.Type))!;

        Assert.Equal(512, property.GetMaxLength());
    }

    // A plain DbContext, as TA's is: every string 1000 long unless it says otherwise, and the queue mapped by hand.
    private sealed class BoundedStringsContext(DbContextOptions<BoundedStringsContext> options) : DbContext(options)
    {
        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.Properties<string>().HaveMaxLength(1000);

        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddRaskJobs();
    }
}
