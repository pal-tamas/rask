using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Rask.Data.Tests;

// HasJsonIndex (#1112) on a provider that does not create it: refused at boot, like the other declarations a provider
// would silently ignore — without the index every filter on the path would still work, and read the whole table.
[Collection(DataDbCollection.Name)]
public sealed class JsonIndexCheckTests
{
    [Fact]
    public async Task A_json_index_the_provider_would_ignore_fails_the_boot()
    {
        var services = new ServiceCollection();
        services.AddRaskData<JsonIndexedContext>();
        services.AddDbContextFactory<JsonIndexedContext>(o => o.UseSqlite("Data Source=:memory:"));
        await using var provider = services.BuildServiceProvider();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            foreach (var hosted in provider.GetServices<IHostedService>())
            {
                await hosted.StartAsync(CancellationToken.None);
            }
        });

        Assert.Contains("HasJsonIndex", error.Message, StringComparison.Ordinal);
        Assert.Contains("UseRaskSqlite(services)", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_path_that_is_not_a_member_chain_into_the_entity_is_refused_where_it_is_written()
    {
        var builder = new ModelBuilder();

        // The JSON column itself, and a call rather than a member: neither names a value inside the JSON. (A chain
        // that names the wrong members is caught when the migration resolves it against the model.)
        Assert.Contains("ToJson()", Assert.Throws<ArgumentException>(() =>
            builder.Entity<JsonDoc>().HasJsonIndex(d => d.Meta)).Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() =>
            builder.Entity<JsonDoc>().HasJsonIndex(d => d.Meta.Status.ToUpperInvariant()));
    }

    [Fact]
    public void Declaring_a_path_twice_records_it_once()
    {
        var builder = new ModelBuilder();
        var entity = builder.Entity<JsonDoc>();

        entity.HasJsonIndex(d => d.Meta.Status).HasJsonIndex(d => d.Meta.Status);

        Assert.Equal("v1|Meta.Status", entity.Metadata.FindAnnotation(JsonIndexSpec.AnnotationName)?.Value);
    }

    public sealed class JsonDoc
    {
        public int Id { get; set; }

        public string Title { get; set; } = "";

        public JsonMeta Meta { get; set; } = new();
    }

    public sealed class JsonMeta
    {
        public string Status { get; set; } = "";
    }

    private sealed class JsonIndexedContext(DbContextOptions<JsonIndexedContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<JsonDoc>(doc =>
            {
                doc.OwnsOne(d => d.Meta, meta => meta.ToJson());
                doc.HasJsonIndex(d => d.Meta.Status);
            });
    }
}
