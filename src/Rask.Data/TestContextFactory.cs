using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data;

/// <summary>The <see cref="TestDatabase{TContext}.Factory" />: a fresh context over the fixture's database per call.</summary>
internal sealed class TestContextFactory<[DynamicallyAccessedMembers(DataTrimming.Context)] TContext>(Func<TContext> create) : IDbContextFactory<TContext>
    where TContext : DbContext
{
    public TContext CreateDbContext() => create();
}
