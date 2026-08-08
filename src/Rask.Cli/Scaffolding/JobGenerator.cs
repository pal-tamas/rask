namespace Rask.Cli.Scaffolding;

/// <summary>
/// Scaffolds a background job under <c>Features/Shared/</c> — or into a feature slice <c>Features/&lt;Feature&gt;/</c>
/// when <c>--feature</c> names one (or an explicit <c>--output</c> dir): an <c>IJob</c> record and its
/// <c>ICommandHandler</c>, plus the <c>Rask.Jobs</c> / <c>Rask.Cqrs</c> packages and the registration steps.
/// </summary>
internal static class JobGenerator
{
    public static ScaffoldResult Generate(
        ProjectContext project, string baseDirectory, string name, string? feature, string? outputOverride)
    {
        var targetDirectory = Scaffold.TargetDirectory(baseDirectory, outputOverride, Scaffold.FeatureOrShared(feature));
        var file = new ScaffoldFile(Path.Combine(targetDirectory, name + ".cs"), Render(project.NamespaceFor(targetDirectory), name));
        return new ScaffoldResult([file], Notes(name, project.Database, project.IsBrowser))
        {
            // Rask.Jobs pulls in the queue/processor; Rask.Cqrs provides the ICommandHandler the job
            // dispatches to. A browser app needs one more: without Rask.SQLite.Browser its database lives
            // in the runtime's in-memory filesystem and every queued job is lost on reload.
            Packages = project.IsBrowser
                ? ["Rask.Cqrs", "Rask.Jobs", "Rask.SQLite.Browser"]
                : ["Rask.Cqrs", "Rask.Jobs"],
        };
    }

    /// <summary>Render the job + handler source. Pure, so it is unit-tested directly.</summary>
    internal static string Render(string @namespace, string name) =>
        $$"""
        namespace {{@namespace}};

        /// <summary>A background job — enqueue it with <see cref="IJobQueue"/> and it runs off the request thread.</summary>
        public sealed record {{name}} : IJob;

        public sealed class {{name}}Handler : ICommandHandler<{{name}}>
        {
            public Task HandleAsync({{name}} job, CancellationToken cancellationToken)
            {
                // TODO: do the work.
                return Task.CompletedTask;
            }
        }

        """;

    /// <summary>The "register it and create the schema" next-steps printed after scaffolding.</summary>
    /// <remarks>
    /// Branches on the project kind because the server steps are not merely unhelpful in a browser app,
    /// they are impossible: <c>rask db</c> wraps <c>dotnet-ef</c> against a design-time database, and a
    /// browser bundle has no migrations assembly. Worse, following them appears to work — the app builds
    /// and runs, and silently loses every queued job on reload, because nothing persists the database.
    /// </remarks>
    internal static string Notes(string name, DatabaseInfo database, bool isBrowser = false) =>
        isBrowser
            ? $"""
              Next steps (browser/WASM app):
                1. Register the services in Program.cs (once), in this order:
                     host.Services.AddRaskBrowserSqlite("app");   // restores + persists the database
                     host.Services.AddDbContextFactory<AppDbContext>(o =>
                         o.UseSqlite(BrowserSqlite.ConnectionString("app")));
                     host.Services.AddRaskCqrs();
                     host.Services.AddRaskJobs<AppDbContext>();
                2. Map the jobs tables in your DbContext's OnModelCreating (once):
                     modelBuilder.AddRaskJobs();
                3. Create the schema at boot — there is no design-time database here, so `rask db` does
                   not apply. Register a hosted service after AddRaskBrowserSqlite that calls
                   EnsureCreatedAsync (or run migrations yourself).
                4. Enqueue it anywhere IJobQueue is injected:
                     await jobs.EnqueueAsync(new {name}());

              Two things this app must get right, or the job queue will not survive a reload:
                - Publish WITHOUT -p:WasmBuildNative=false. SQLite is a native library; skipping the
                  relink gives you a bundle that boots and then fails on every database call.
                - Set <PublishTrimmed>false</PublishTrimmed>. EF Core does not survive the trimmer in a
                  browser build.
              See docs/sqlite.md and samples/Rask.Example.Wasm.Jobs for a working app.
              """
            : $"""
              Next steps:
                1. Register the services in Program.cs (once):
                     builder.Services.AddRaskCqrs();
                     builder.Services.AddRaskJobs<AppDbContext>();   // your DbContext
                     builder.Services.AddDbContextFactory<AppDbContext>(o => o.{database.UseMethod}("{database.DefaultConnectionString}"));
                2. Map the jobs tables in your DbContext's OnModelCreating (once):
                     modelBuilder.AddRaskJobs();
                3. Create the schema:
                     rask db add AddJobs && rask db update
                4. Enqueue it anywhere IJobQueue is injected:
                     await jobs.EnqueueAsync(new {name}());
              """;
}
