using Microsoft.EntityFrameworkCore;

namespace Rask.Benchmarks.Sqlite.Db;

/// <summary>The mixed workload's model, mapped onto the same <c>posts</c> table the raw arm uses.</summary>
internal sealed class PostsDbContext(DbContextOptions<PostsDbContext> options) : DbContext(options)
{
    internal DbSet<Post> Posts => Set<Post>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var post = modelBuilder.Entity<Post>();
        post.ToTable("posts");
        post.Property(p => p.Id).HasColumnName("id");
        post.Property(p => p.Title).HasColumnName("title");
        post.Property(p => p.Body).HasColumnName("body");
        post.Property(p => p.CreatedAt).HasColumnName("created_at");
        post.HasIndex(p => p.CreatedAt).HasDatabaseName("ix_posts_created");
    }
}

internal sealed class Post
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public long CreatedAt { get; set; }
}
