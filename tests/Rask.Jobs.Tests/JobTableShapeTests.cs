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
}
