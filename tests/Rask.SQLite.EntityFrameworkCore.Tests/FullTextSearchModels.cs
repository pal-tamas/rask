using Microsoft.EntityFrameworkCore;
using Rask.Data;

namespace Rask.SQLite.EntityFrameworkCore.Tests;

// Test models for FullTextSearchTests. EF caches a built model per context type, so each model variant needs its own
// context type — two instances of one type would silently share a cached model.
internal sealed class Article
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public bool Published { get; set; }

    public string? Tag { get; set; }
}

// The table before anyone asked for search.
internal class PlainArticleContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<Article> Articles => Set<Article>();
}

internal class ArticleContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<Article> Articles => Set<Article>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Article>().HasFullTextSearch(a => new { a.Title, a.Body });
}

// Same index, but Tag becomes required — a change SQLite cannot apply in place, so EF rebuilds the table.
internal sealed class RebuiltArticleContext(DbContextOptions options) : ArticleContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Article>().Property(a => a.Tag).IsRequired().HasDefaultValue(string.Empty);
    }
}

// Same index, but Body's column is renamed — an external-content index would otherwise read a column that is gone.
internal sealed class RenamedArticleContext(DbContextOptions options) : ArticleContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Article>().Property(a => a.Body).HasColumnName("Content");
    }
}

// The index narrowed to the title only: a changed declaration, not a new one.
internal sealed class TitleOnlyArticleContext(DbContextOptions options) : PlainArticleContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Article>().HasFullTextSearch(a => a.Title);
}

internal sealed class PublishedArticleContext(DbContextOptions options) : ArticleContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Article>().HasQueryFilter(a => a.Published);
    }
}

internal sealed class EnglishArticleContext(DbContextOptions options) : PlainArticleContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Article>().HasFullTextSearch(a => new { a.Title, a.Body }, FullTextTokenizer.English);
}

// A strongly-typed id, converted the two ways a model can say it: a converter instance, and a converter TYPE through
// ConfigureConventions (how Rask.Data's generated registry maps ids). Either way the store key is an INTEGER rowid.
internal readonly record struct TicketId(int Value);

internal sealed class TicketIdConverter() : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<TicketId, int>(
    id => id.Value,
    value => new TicketId(value));

internal sealed class Ticket
{
    public TicketId Id { get; set; }

    public string Text { get; set; } = string.Empty;
}

internal sealed class TicketContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<Ticket> Tickets => Set<Ticket>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Ticket>().Property(t => t.Id).HasConversion(id => id.Value, value => new TicketId(value));
        modelBuilder.Entity<Ticket>().HasFullTextSearch(t => t.Text);
    }
}

internal sealed class ConventionTicketContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<Ticket> Tickets => Set<Ticket>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.Properties<TicketId>().HaveConversion<TicketIdConverter>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Ticket>().HasFullTextSearch(t => t.Text);
}

// An integer key whose layout was recorded as the key map — what a migration saved by an earlier model can carry. The
// recorded choice has to win on BOTH sides, or the query joins to a table the migration did not build.
internal sealed class RecordedKeyMapArticleContext(DbContextOptions options) : PlainArticleContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Article>()
            .HasAnnotation("Rask:FullTextSearch:Layout", "keys")
            .HasFullTextSearch(a => new { a.Title, a.Body });
}

// A hierarchy sharing one table (TPH): the base declares the index, the derived type is searched.
internal class Page
{
    public int Id { get; set; }

    public string Text { get; set; } = string.Empty;
}

internal sealed class HelpPage : Page
{
    public string Topic { get; set; } = string.Empty;
}

internal sealed class PageContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<Page> Pages => Set<Page>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Page>().HasFullTextSearch(p => p.Text);
        modelBuilder.Entity<HelpPage>();
    }
}

// A Guid key: SQLite's implicit rowid is not stable across VACUUM, so the index goes through a key map.
internal sealed class Memo
{
    public Guid Id { get; set; }

    public string Text { get; set; } = string.Empty;
}

internal sealed class MemoContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<Memo> Memos => Set<Memo>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Memo>().HasFullTextSearch(m => m.Text);
}

internal sealed class Entry
{
    public int TenantId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
}

internal sealed class EntryContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<Entry> Entries => Set<Entry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Entry>().HasKey(e => new { e.TenantId, e.Code });
        modelBuilder.Entity<Entry>().HasFullTextSearch(e => e.Text);
    }
}
