namespace Rask.Cli.Scaffolding;

/// <summary>
/// Scaffolds a background job under <c>Jobs/</c> (or an explicit output dir): an <c>IJob</c> record and its
/// <c>ICommandHandler</c>, plus the <c>Rask.Jobs</c> / <c>Rask.Cqrs</c> packages and the registration steps.
/// </summary>
internal static class JobGenerator
{
    public static ScaffoldResult Generate(ProjectContext project, string baseDirectory, string name, string? outputOverride)
    {
        var targetDirectory = Scaffold.TargetDirectory(baseDirectory, outputOverride, "Jobs");
        var file = new ScaffoldFile(Path.Combine(targetDirectory, name + ".cs"), Render(project.NamespaceFor(targetDirectory), name));
        return new ScaffoldResult([file], Notes(name))
        {
            // Rask.Jobs pulls in the queue/processor; Rask.Cqrs provides the ICommandHandler the job dispatches to.
            Packages = ["Rask.Cqrs", "Rask.Jobs"],
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
    internal static string Notes(string name) =>
        $"""
        Next steps:
          1. Register the services in Program.cs (once):
               builder.Services.AddRaskCqrs();
               builder.Services.AddRaskJobs<AppDbContext>();   // your DbContext
               builder.Services.AddDbContextFactory<AppDbContext>(o => o.UseSqlite("Data Source=app.db"));
          2. Map the jobs tables in your DbContext's OnModelCreating (once):
               modelBuilder.AddRaskJobs();
          3. Create the schema:
               rask db add AddJobs && rask db update
          4. Enqueue it anywhere IJobQueue is injected:
               await jobs.EnqueueAsync(new {name}());
        """;
}
