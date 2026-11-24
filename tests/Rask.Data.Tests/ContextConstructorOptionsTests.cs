using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Rask.Data.Tests;

/// <summary>
/// EF Core builds a context's internal services in its constructor from the options it is handed, and again from the
/// ones <c>OnConfiguring</c> leaves. A Rask context handed plain options is configured before the first, so the two
/// are one set — EF Core compares them with the options' own equality.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class ContextConstructorOptionsTests
{
    [Fact]
    public void A_write_context_handed_plain_options_leaves_them_as_its_configuring_finds_them()
    {
        using var db = new OptionsSpyContext(OptionsLayout.LikeAddDbContext<OptionsSpyContext>().UseSqlite("Data Source=:memory:").Options);

        _ = db.GetInfrastructure();

        Assert.Equal(db.Handed, db.Configured);
    }

    [Fact]
    public void A_read_context_handed_plain_options_leaves_them_as_its_configuring_finds_them()
    {
        using var db = new ReadOptionsSpyContext(
            OptionsLayout.LikeAddDbContext<ReadOptionsSpyContext>().UseSqlite("Data Source=:memory:").Options);

        _ = db.GetInfrastructure();

        Assert.Equal(db.Handed, db.Configured);
    }

    [Fact]
    public void Options_a_host_already_configured_are_handed_on_as_they_are()
    {
        var builder = OptionsLayout.LikeAddDbContext<OptionsSpyContext>().UseSqlite("Data Source=:memory:");
        using (var first = new OptionsSpyContext(builder.Options))
        {
            _ = first.GetInfrastructure();
            builder = new DbContextOptionsBuilder<OptionsSpyContext>((DbContextOptions<OptionsSpyContext>)first.Configured!);
        }

        using var db = new OptionsSpyContext(builder.Options);
        _ = db.GetInfrastructure();

        Assert.Same(builder.Options, db.Handed);
    }

    private sealed class OptionsSpyContext(DbContextOptions<OptionsSpyContext> options) : RaskDbContext(options)
    {
        public IDbContextOptions? Handed { get; private set; }

        public IDbContextOptions? Configured { get; private set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            Handed = optionsBuilder.Options;
            base.OnConfiguring(optionsBuilder);
            Configured = optionsBuilder.Options;
        }
    }

    private sealed class ReadOptionsSpyContext(DbContextOptions<ReadOptionsSpyContext> options) : RaskReadDbContext(options)
    {
        public IDbContextOptions? Handed { get; private set; }

        public IDbContextOptions? Configured { get; private set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            Handed = optionsBuilder.Options;
            base.OnConfiguring(optionsBuilder);
            Configured = optionsBuilder.Options;
        }
    }
}
