using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Rask.Data;
using Rask.Query;

namespace Rask.Tests;

/// <summary>
///     A write refreshes the queries about what it wrote, in the session that made it — with nothing wired by
///     the app.
/// </summary>
public sealed class DataQueryInvalidationTests
{
    private sealed record Person;

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options);

    private static readonly QueryOptions Keep = new() { StaleTime = TimeSpan.FromHours(1) };

    private static Microsoft.AspNetCore.Builder.WebApplication Built()
    {
        var app = RaskApp.Create([], b => b.WebHost.UseSetting("urls", "http://127.0.0.1:0"));
        app.Services.AddDbContextFactory<TestDbContext>(o => o.UseSqlite("Data Source=:memory:"));
        return app.Build<TestApp>();
    }

    [Fact]
    public void An_app_that_says_nothing_saves_through_the_change_reporter()
    {
        using var scope = Built().Services.CreateScope();

        // Every context takes its interceptors from this registration — the app's own and the batteries'.
        Assert.Contains(
            scope.ServiceProvider.GetServices<ISaveChangesInterceptor>(),
            i => i.GetType().Name == "DataChangesInterceptor");
        Assert.IsType<QueryDataChanges>(Assert.Single(scope.ServiceProvider.GetServices<IDataChanges>()));
    }

    [Fact]
    public async Task A_write_invalidates_the_writing_sessions_queries_about_that_type_and_no_other_sessions()
    {
        var services = Built().Services;
        using var mine = services.CreateScope();
        using var theirs = services.CreateScope();
        var loads = 0;
        var theirLoads = 0;
        using var people = mine.ServiceProvider.GetRequiredService<IQueryClient>()
            .Query(QueryKey.For<Person>("active"), _ => Task.FromResult(++loads), Keep);
        using var otherPeople = theirs.ServiceProvider.GetRequiredService<IQueryClient>()
            .Query(QueryKey.For<Person>("active"), _ => Task.FromResult(++theirLoads), Keep);
        await Settled(people);
        await Settled(otherPeople);

        // What the interceptor calls once the save commits, with the session's scope ambient.
        foreach (var observer in mine.ServiceProvider.GetServices<IDataChanges>())
        {
            observer.Saved([typeof(Person)]);
        }

        await Settled(people);
        await Settled(otherPeople);

        Assert.Equal(2, people.Data);
        Assert.Equal(1, otherPeople.Data);
    }

    /// <summary>A plain EF row: not a Rask aggregate, so it is reported as itself.</summary>
    public sealed class Note
    {
        public int Id { get; set; }

        public string Text { get; set; } = "";
    }

    private sealed class NotesContext(DbContextOptions<NotesContext> options) : DbContext(options)
    {
        public DbSet<Note> Notes => Set<Note>();
    }

    [Fact]
    public async Task A_real_save_in_a_session_refetches_that_sessions_query_about_the_type()
    {
        var app = RaskApp.Create([], b => b.WebHost.UseSetting("urls", "http://127.0.0.1:0"));
        app.Services.AddDbContextFactory<TestDbContext>(o => o.UseSqlite("Data Source=:memory:"));
        // Wired the way every scaffolded Program.cs wires the app's context. A file, not :memory:, because
        // an in-memory database is gone when EnsureCreated closes its connection.
        var file = Path.Combine(Path.GetTempPath(), $"rask-notes-{Guid.NewGuid():N}.db");
        app.Services.AddDbContextFactory<NotesContext>((sp, o) => o
            .UseSqlite($"Data Source={file};Pooling=False")
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
        var services = app.Build<TestApp>().Services;
        using var session = services.CreateScope();
        var loads = 0;
        using var notes = session.ServiceProvider.GetRequiredService<IQueryClient>()
            .Query(QueryKey.For<Note>(), _ => Task.FromResult(++loads), Keep);
        await Settled(notes);

        // What the host does around a session's render and every handler it dispatches.
        using (Db.UseScope(session.ServiceProvider))
        {
            await using var db = await services.GetRequiredService<IDbContextFactory<NotesContext>>().CreateDbContextAsync();
            await db.Database.EnsureCreatedAsync();
            db.Notes.Add(new Note { Text = "hello" });
            await db.SaveChangesAsync();
        }

        await Settled(notes);
        File.Delete(file);

        Assert.Equal(2, notes.Data);
    }

    private static async Task Settled<T>(Query<T> query)
    {
        _ = query.Data;
        for (var i = 0; i < 50 && query.IsFetching; i++)
        {
            await Task.Delay(10);
        }
    }
}
