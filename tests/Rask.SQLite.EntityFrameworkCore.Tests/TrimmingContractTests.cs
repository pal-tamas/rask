using System.Reflection;
using Rask.SQLite;

namespace Rask.SQLite.EntityFrameworkCore.Tests;

// #1132: the package is built IsTrimmable, so the trim analyzer runs on it under warnings-as-errors and a trimmed app
// that references it needs nothing kept by hand. Removing the property would silently switch that analysis off —
// every other gate stays green, and the first sign is a trimmed browser app failing at its first search.
public sealed class TrimmingContractTests
{
    [Fact]
    public void The_sqlite_provider_is_built_so_a_trimmed_app_keeps_it_whole()
    {
        var metadata = typeof(RaskSqliteDbContextOptionsExtensions).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(a => a.Key == "IsTrimmable");

        Assert.NotNull(metadata);
        Assert.Equal("True", metadata.Value);
    }
}
