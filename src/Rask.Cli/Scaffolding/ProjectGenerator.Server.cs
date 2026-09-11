using System.Text;

namespace Rask.Cli.Scaffolding;

// The server template: an ASP.NET live-server Rask app.
internal static partial class ProjectGenerator
{
    /// <summary>Generates the <c>server</c> template into <paramref name="targetDirectory"/>.</summary>
    public static ScaffoldResult GenerateServer(
        string targetDirectory,
        string name,
        ServerBatteries batteries,
        string version,
        IReadOnlyList<string>? islands = null)
    {
        ArgumentNullException.ThrowIfNull(batteries);

        // Apply the flags' implications once, up front, so the template's conditions are evaluated
        // against the resolved set (--jobs means --data means --cqrs, --push means --pwa, …). See
        // ServerBatteries.Normalized. The template tree marks each region with the WEAKEST condition
        // that describes it, which is only correct if implications have already been applied here.
        batteries = batteries.Normalized();

        return new ScaffoldResult(
            TemplateMaterializer.Files(targetDirectory, "server", name, batteries, version, islands),
            ServerNextSteps(name, batteries))
        {
            Packages = ServerPackages(batteries),
        };
    }

    // The package list, in the same order the csproj emits them, so `rask new`'s summary matches the file.
    private static List<string> ServerPackages(ServerBatteries batteries)
    {
        // No Rask.Tailwind here: the Tailwind build ships INSIDE Rask.Server (RaskTailwindBuildPack),
        // so a scaffolded csproj naming it would be a second copy of the same targets, imported twice.
        //
        // Rask.Ui IS named, and directly rather than through the meta-package, because a package's
        // build/ hooks are imported for a DIRECT reference only — and those hooks are what put daisyUI's
        // plugin next to Styles/app.css and the kit's sheet in wwwroot. It also brings the ~110 Ui*
        // components, which is a bonus here rather than the reason: the starter page writes daisyUI's
        // own class names, so it needs the plugin whether or not it ever names a component.
        //
        // Rask.DevTools is named directly for the same build/-hooks reason: its targets are what keep the
        // devtools out of a Release publish, and an app that does not reference the `Rask` meta-package —
        // which is every scaffolded one — would otherwise never get them.
        var packages = new List<string> { "Rask.Server", "Rask.Ui", "Rask.DevTools" };

        if (batteries.Cqrs)
        {
            packages.Add("Rask.Cqrs");

            // Not a flag of its own. A dispatcher without a cache means every render refetches, and the
            // first thing anyone building a page over IDispatcher needs is the thing that stops that —
            // so it arrives wired rather than as something to discover in the docs later.
            packages.Add("Rask.Query");
        }

        if (batteries.Data)
        {
            packages.Add("Rask.Data");
            packages.Add("Rask.SQLite.EntityFrameworkCore");

            // Continuous backup. Referenced whenever there's a database: the wiring in Program.cs stays
            // inert until Rask:Litestream:ReplicaUrl is set, so this costs an unused reference and buys a
            // one-env-var path from "single copy on one disk" to "the box is disposable".
            packages.Add("Rask.SQLite.Litestream");

            // Accounts. Paired with the database rather than with a flag, because AppDbContextCs maps
            // the account tables whenever there is a context — the two have to move together or the
            // generated `using Rask.Auth;` does not compile.
            packages.Add("Rask.Auth");
        }

        if (batteries.Outbox)
        {
            packages.Add("Rask.Outbox");
        }

        if (batteries.Jobs)
        {
            packages.Add("Rask.Jobs");
        }

        if (batteries.Mail)
        {
            packages.Add("Rask.Mail");
        }

        if (batteries.Cache)
        {
            packages.Add("Rask.Cache");
        }

        if (batteries.Storage)
        {
            packages.Add("Rask.Storage");
        }

        if (batteries.AnySqliteOps)
        {
            packages.Add("Rask.SQLite.Snapshots");
        }

        if (batteries.Logs)
        {
            packages.Add("Rask.Logging");
        }

        if (batteries.Push)
        {
            packages.Add("Rask.WebPush");
        }

        if (batteries.Ops)
        {
            packages.Add("Rask.Dashboard");
        }

        if (batteries.Wasm)
        {
            packages.Add("Rask.Wasm.Hosting");

            if (batteries.Cqrs)
            {
                // The endpoint half. Its counterpart, Rask.Cqrs.Client, is declared as a
                // browser-only reference so it never reaches this process.
                packages.Add("Rask.Cqrs.Server");
            }
        }

        return packages;
    }

    private static string ServerNextSteps(string name, ServerBatteries batteries)
    {
        var steps = new StringBuilder();
        steps.Append("Created ").Append(name).Append(" (Rask server app).\n\nNext steps:\n");
        steps.Append("  cd ").Append(name).Append('\n');
        steps.Append("  rask dev            # run with hot reload (or: dotnet run)\n");
        if (batteries.Docker)
        {
            steps.Append("  docker build -t ").Append(name.ToLowerInvariant()).Append(" .   # then: docker run -p 8080:8080 …\n");
        }

        // Nothing about migrations here any more. `rask new` creates and applies the first one itself, so
        // by the time this text is printed the tables the pillars need already exist — and repeating the
        // commands would read as work still to do. The command prints the manual pair only in the two
        // cases where it could not run them: --no-restore, and a migration that failed.
        if (batteries.Data)
        {
            steps.Append("\nThe first migration is already applied to app.db. For your first entity, declare\n");
            steps.Append("a class deriving from Model<TId> — no DbSet, no configuration class, no registration:\n");
            steps.Append("\n  public sealed class Product : Model<Guid>\n");
            steps.Append("  {\n");
            steps.Append("      public string Name { get; private set; } = \"\";\n");
            steps.Append("  }\n");
            steps.Append("\nThen `rask db add <Name>` and `rask db update` to migrate it. Query it off the type\n");
            steps.Append("itself: Product.Where(...), Product.FindAsync(id), Product.Add(p).\n");
        }

        if (batteries.Push)
        {
            steps.Append("\nWeb Push needs a VAPID key pair. Generate one and save it to user-secrets:\n");
            steps.Append("  dotnet user-secrets set \"Rask:WebPush:VapidKeys:PublicKey\" \"<public>\"\n");
            steps.Append("  dotnet user-secrets set \"Rask:WebPush:VapidKeys:PrivateKey\" \"<private>\"\n");
            steps.Append("  (VapidKeys.Generate() prints a pair; the private key must never be served.)\n");
        }

        return steps.ToString();
    }

    private static string AppDbContextCs(ServerBatteries batteries)
    {
        var usings = new StringBuilder("using Microsoft.EntityFrameworkCore;\nusing Rask.Data;\n");
        var schema = new StringBuilder();

        // Each pillar owns a table (or two) in the app's own database. These calls only add the framework
        // entities to the model; `rask db add` then writes the migration that creates them.
        if (batteries.Outbox)
        {
            usings.Append("using Rask.Outbox;\n");
            schema.Append("\n        modelBuilder.AddRaskOutbox();");
        }

        if (batteries.Jobs)
        {
            usings.Append("using Rask.Jobs;\n");
            schema.Append("\n        modelBuilder.AddRaskJobs();");
        }

        if (batteries.Mail)
        {
            usings.Append("using Rask.Mail;\n");
            schema.Append("\n        modelBuilder.AddRaskMail();");
        }

        if (batteries.Cache)
        {
            usings.Append("using Rask.Cache;\n");
            schema.Append("\n        modelBuilder.AddRaskCache();");
        }

        if (batteries.Storage)
        {
            usings.Append("using Rask.Storage;\n");
            schema.Append("\n        modelBuilder.AddRaskStorage();");
        }

        // Accounts, unconditionally: the auth battery is ON by default, so every app with a database
        // has one. Mapping these is not optional the way the pillars above are — AddRaskAuth registers
        // Identity's EF stores against this context, so without the tables the app boots happily and
        // then fails at the first registration on a missing AspNetUsers.
        //
        // Mapped even when an app writes c.Auth.Off(), which is the documented behaviour for every
        // database-backed battery: turning one off must not produce a destructive migration.
        usings.Append("using Rask.Auth;\n");
        schema.Append("\n        modelBuilder.AddRaskAuth();");

        return $$"""
        {{usings.ToString().TrimEnd('\n')}}

        namespace Company.RaskServer.Features.Shared;

        public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : RaskDbContext(options)
        {
            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                // RaskDbContext, not DbContext: the base maps every class deriving from Model<TId>, which
                // is what lets you declare an entity and nothing else — no DbSet property, no
                // IEntityTypeConfiguration, no registration. It also brings the value converters for
                // strongly-typed ids, which EF reads before the model is built. Over plain DbContext this
                // file still compiles and every model you declare is silently absent from the database.
                base.OnModelCreating(modelBuilder);

                // ApplyRaskConventions walks the model as it stands, giving each marked entity its audit
                // stamps, its soft-delete query filter and its concurrency token — so it has to come LAST,
                // after the models, the configurations AND every battery's tables. Anything mapped after
                // it silently misses out, which is what a User declaring ITimestamped used to do.
                modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);{{schema}}
                modelBuilder.ApplyRaskConventions();
            }
        }

        """;
    }

    // ---- server-only template files ----

    /// <summary>
    /// The production image. With <c>--data</c> on a file database it also carries the <c>litestream</c>
    /// binary the app's replicator drives: the wiring in Program.cs is inert without it, so shipping one
    /// without the other would mean a "continuous backup" that silently never runs. Copied from Litestream's
    /// own published image — one layer, no package manager, and the right architecture picked by the manifest.
    /// </summary>
    /// <remarks>
    /// Both spliced extras exist because the database is a <em>file on this box</em>, so a client-server
    /// database gets neither. The two gates differ on purpose: the <c>/data</c> mount point tracks whatever
    /// <c>rask deploy</c> mounts — which includes an app that has no <c>--data</c> yet, so adding it later
    /// doesn't need a new Dockerfile — while the binary is only worth carrying once there is a database to
    /// replicate.
    /// </remarks>
    /// <summary>
    /// A starter catalog. The neutral one carries the app's English; a translation starts as a copy so
    /// the keys line up and the build tells you which ones still need doing (RASK052).
    /// </summary>
    private static string StringsCatalog(bool neutral) =>
        neutral
            ? """
              {
                "AppTitle": "Welcome to Rask",
                "Greeting": "Hello, {name}!",
                "Items": { "$plural": "count", "one": "{count} item", "other": "{count} items" }
              }
              """
            : """
              // Translated text for this language. The keys come from the neutral catalog; one that is
              // missing here is a warning (RASK052) and falls back to the neutral text, so a
              // half-finished translation still renders.
              {
                "AppTitle": "Welcome to Rask",
                "Greeting": "Hello, {name}!",
                "Items": { "$plural": "count", "one": "{count} item", "other": "{count} items" }
              }
              """;

    private static string Dockerfile(ServerBatteries batteries)
    {
        return Splice(Splice(DockerfileTemplate, "@@LITESTREAM@@", batteries.Data ? LitestreamLayer : null),
            "@@DATADIR@@", DataDirectoryLayer);

        // Fill the slot, or drop the marker and the line it sits on so an unused slot leaves no stray blank.
        static string Splice(string template, string marker, string? content) =>
            content is null
                ? template.Replace(marker + "\n", string.Empty, StringComparison.Ordinal)
                    .Replace(marker, string.Empty, StringComparison.Ordinal)
                : template.Replace(marker, content, StringComparison.Ordinal);
    }

    // Both layers carry their own leading blank line so a filled slot is separated from what precedes it,
    // and an empty slot collapses cleanly instead of leaving one behind.
    private const string LitestreamLayer =
        "\n# The replicator binary Program.cs drives when Rask__Litestream__ReplicaUrl is set (see docs/sqlite.md).\n"
        + "COPY --from=litestream/litestream:0.3.13 /usr/local/bin/litestream /usr/local/bin/litestream\n";

    private const string DataDirectoryLayer =
        "\n# A writable data directory for the SQLite database, owned by the image's non-root runtime user\n"
        + "# ($APP_UID). `rask deploy` mounts a named volume here and points the app at /data/app.db (via\n"
        + "# Rask:ConnectionStrings:App), so the database survives container replacement across redeploys. A fresh\n"
        + "# named volume inherits this directory's ownership, so the non-root app can create app.db in it.\n"
        + "USER root\n"
        + "RUN mkdir -p /data && chown $APP_UID:$APP_UID /data\n"
        + "USER $APP_UID";

    private const string DockerfileTemplate =
        """
        # Multi-stage build: compile on the .NET SDK image, run on the smaller aspnet runtime.
        # The aspnet:10.0 image already runs as a non-root user and listens on port 8080
        # (ASPNETCORE_HTTP_PORTS=8080) — no extra hardening needed for a basic deploy.
        FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
        WORKDIR /src

        # Restore first (cached layer): only the csproj invalidates it, so code edits reuse the cache.
        COPY ["Company.RaskServer.csproj", "./"]
        RUN dotnet restore
        COPY . .
        RUN dotnet publish "Company.RaskServer.csproj" -c Release -o /app --no-restore

        FROM mcr.microsoft.com/dotnet/aspnet:10.0
        WORKDIR /app
        COPY --from=build /app .
        @@LITESTREAM@@
        @@DATADIR@@

        EXPOSE 8080
        # The app calls UseHttpsRedirection(); inside the container no HTTPS port is configured,
        # so it no-ops. Terminate TLS at your reverse proxy / ingress and forward plain HTTP to 8080.
        ENTRYPOINT ["dotnet", "Company.RaskServer.dll"]

        """;
}
