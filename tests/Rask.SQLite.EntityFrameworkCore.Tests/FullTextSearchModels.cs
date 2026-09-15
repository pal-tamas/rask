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
