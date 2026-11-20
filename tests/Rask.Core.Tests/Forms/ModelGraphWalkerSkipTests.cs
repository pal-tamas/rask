using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Rask.Core.Forms;

namespace Rask.Core.Tests.Forms;

// What validation's walk leaves alone: what is marked [SkipValidation] (a property, or every object of a type), and an
// Entity Framework navigation that is not loaded — reading it would load it, a query per check.
public sealed class ModelGraphWalkerSkipTests
{
    [Fact]
    public void A_property_marked_skip_validation_is_not_walked_into()
    {
        var model = new Order { Lines = [new Line()], Audit = new Line() };

        var walked = ModelGraphWalker.Walk(model).ToList();

        Assert.Contains(model.Lines[0], walked);
        Assert.DoesNotContain(model.Audit, walked);
    }

    [Fact]
    public void An_object_of_a_type_marked_skip_validation_is_not_walked_into()
    {
        var model = new WithCache { Cache = new Cache { Inner = new Line() } };

        var walked = ModelGraphWalker.Walk(model).ToList();

        Assert.DoesNotContain(model.Cache, walked);
        Assert.DoesNotContain(model.Cache.Inner, walked);
    }

    [Fact]
    public void The_model_itself_is_walked_even_when_its_type_is_marked()
    {
        var model = new Cache { Inner = new Line() };

        var walked = ModelGraphWalker.Walk(model).ToList();

        Assert.Contains(model, walked);
    }

    [Fact]
    public async Task An_entity_framework_navigation_that_is_not_loaded_is_not_loaded_by_validation()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using (var seed = new BlogContext(connection))
        {
            await seed.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            seed.Add(new Blog { Name = "news", Posts = [new Post { Title = "first" }] });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var context = new BlogContext(connection);
        var blog = await context.Set<Blog>().SingleAsync(TestContext.Current.CancellationToken);

        var walked = ModelGraphWalker.Walk(blog).ToList();

        Assert.False(context.Entry(blog).Collection(b => b.Posts).IsLoaded);
        Assert.Single(walked);
    }

    [Fact]
    public async Task An_entity_framework_navigation_that_is_loaded_is_walked()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using (var seed = new BlogContext(connection))
        {
            await seed.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            seed.Add(new Blog { Name = "news", Posts = [new Post { Title = "first" }] });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var context = new BlogContext(connection);
        var blog = await context.Set<Blog>().Include(b => b.Posts).SingleAsync(TestContext.Current.CancellationToken);

        var walked = ModelGraphWalker.Walk(blog).ToList();

        Assert.Contains(walked, static node => node is Post { Title: "first" });
    }

    private sealed class Order
    {
        public List<Line> Lines { get; set; } = [];

        [SkipValidation]
        public Line? Audit { get; set; }
    }

    private sealed class WithCache
    {
        public Cache? Cache { get; set; }
    }

    [SkipValidation]
    private sealed class Cache
    {
        public Line? Inner { get; set; }
    }

    private sealed class Line
    {
        public string Name { get; set; } = "";
    }

    private sealed class BlogContext(SqliteConnection connection) : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(connection);

        protected override void OnModelCreating(ModelBuilder model) => model.Entity<Blog>().HasMany(b => b.Posts).WithOne();
    }

    // EF's "lazy loading without proxies" pattern: the context hands the entity its ILazyLoader.
    public sealed class Blog
    {
        private ICollection<Post>? _posts;

        public Blog()
        {
        }

        private Blog(ILazyLoader lazyLoader) => LazyLoader = lazyLoader;

        public int Id { get; set; }

        public string Name { get; set; } = "";

        public ICollection<Post> Posts
        {
            get => LazyLoader.Load(this, ref _posts) ?? [];
            set => _posts = value;
        }

        private ILazyLoader? LazyLoader { get; set; }
    }

    public sealed class Post
    {
        public int Id { get; set; }

        public string Title { get; set; } = "";
    }
}
