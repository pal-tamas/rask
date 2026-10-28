using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests.Shapes;

// Linked into Rask.Providers.E2E.Tests as well, so the same columns are asked of every provider.

/// <summary>A file handed in with a return: a value, so the collection of them is one JSON column.</summary>
public sealed record FiledFile(string Name, int Pages);

/// <summary>A file attached later. Its own type: the generator pairs a read-only view with the ONE field of its element.</summary>
public sealed record AttachedFile(string Name);

/// <summary>
/// A return with every shape of value collection a model can declare: value objects and plain values, each
/// non-nullable and nullable — the model is the truth, nullability included.
/// </summary>
public sealed class ReturnFiling : Aggregate<int>
{
    public const string Table = "ReturnFilings";

    private readonly List<FiledFile> _files = [];

    private ReturnFiling() { }

    // The read-only view Rask recommends, with the list EF Core writes through behind it.
    public IReadOnlyList<FiledFile> Files => _files;

    // A list property of its own, nullable.
    public List<AttachedFile>? Attachments { get; private set; }

    public IReadOnlyList<string> Labels { get; private set; } = new List<string>();

    public IReadOnlyList<string>? Aliases { get; private set; }

    public static ReturnFiling Empty() => new();

    public void File(string name, int pages) => _files.Add(new FiledFile(name, pages));

    public void Withdraw() => _files.Clear();

    public void Attach(string name) => (Attachments ??= []).Add(new AttachedFile(name));
}

/// <summary>
/// A get-only view with no list behind it: EF Core cannot write a complex collection through it, so it keeps the
/// owned mapping, whose column is NULL whatever the model says.
/// </summary>
public sealed class DraftFiling : Aggregate<int>
{
    private DraftFiling() { }

    public IReadOnlyList<FiledFile> Files { get; private set; } = new List<FiledFile>();
}

/// <summary>The return mapped by Rask's conventions alone.</summary>
public sealed class FilingContext(DbContextOptions<FilingContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ReturnFiling>().ToTable(ReturnFiling.Table);
        modelBuilder.ApplyRaskConventions();
    }
}

/// <summary>The draft mapped by Rask's conventions alone.</summary>
public sealed class DraftFilingContext(DbContextOptions<DraftFilingContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DraftFiling>();
        modelBuilder.ApplyRaskConventions();
    }
}

/// <summary>
/// The return as Rask mapped its value objects before: an owned JSON collection, whose column is NULL whatever
/// the model says. Stated in the context, so the conventions leave it as written.
/// </summary>
public sealed class OwnedFilingContext(DbContextOptions<OwnedFilingContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ReturnFiling>(b =>
        {
            b.ToTable(ReturnFiling.Table);
            b.OwnsMany(f => f.Files, o => o.ToJson());
            b.OwnsMany(f => f.Attachments, o => o.ToJson());
        });
        modelBuilder.ApplyRaskConventions();
    }
}

/// <summary>What an app adopting Rask already had: the JSON written by hand into a NOT NULL text column.</summary>
public sealed class LegacyFilingRow
{
    public int Id { get; set; }

    public string Files { get; set; } = "[]";

    public string? Attachments { get; set; }

    public List<string> Labels { get; set; } = [];

    public List<string>? Aliases { get; set; }
}

/// <summary>The legacy table, as the adopting app's own context maps it.</summary>
public sealed class LegacyFilingContext(DbContextOptions<LegacyFilingContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<LegacyFilingRow>(b =>
        {
            b.ToTable(ReturnFiling.Table);

            // PostgreSQL stores a JSON column as jsonb; a text column holding JSON would be a different column.
            if (Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                b.Property(r => r.Files).HasColumnType("jsonb");
                b.Property(r => r.Attachments).HasColumnType("jsonb");
            }
        });
}
