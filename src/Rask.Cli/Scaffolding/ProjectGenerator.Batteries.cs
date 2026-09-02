using System.Text;

namespace Rask.Cli.Scaffolding;

internal static partial class ProjectGenerator
{
    /// <summary>
    /// Emits the database registration and every DB-backed battery into a generated <c>Program.cs</c>.
    /// </summary>
    /// <remarks>
    /// Shared by the <c>server</c> template and the front-end templates' ASP.NET host. Both
    /// wire the same <c>AppDbContext</c> through the same <c>AddRaskX</c> calls in the same load-bearing
    /// order — the outbox before the context factory, so its interceptor is in the container when the
    /// factory resolves <c>ISaveChangesInterceptor</c> — so the blocks live here once rather than in two
    /// templates that would drift apart the first time one of them was corrected.
    /// </remarks>
    private static void AppendDatabaseAndBatteries(StringBuilder sb, ServerBatteries batteries)
    {
        if (batteries.Data)
        {
            var sqliteData = """

                // The app's database, on its own disk — no external server. AddRaskData registers the
                // auditing/soft-delete/concurrency/domain-event interceptors; UseRaskSqlite is a drop-in for
                // UseSqlite that also applies the production pragmas (WAL, busy_timeout, foreign_keys). The
                // connection string defaults to a local app.db but honours a ConnectionStrings:App override —
                // `rask deploy` sets that to a path on a mounted volume so the DB survives redeploys.
                // Add a `DbSet<T>` to AppDbContext per entity, then `rask db add <Name>` / `rask db update`
                // to create and apply the migration.
                //
                // strictTables makes SQLite enforce each column's declared type instead of coercing whatever
                // it is handed — without it the text "lots" stores happily in an INTEGER column and surfaces
                // as a cast error much later. It applies to tables as they are created, so it costs nothing
                // here and is awkward to adopt once there is data. Drop it if you need a column type outside
                // SQLite's INT/INTEGER/REAL/TEXT/BLOB/ANY.
                builder.Services.AddRaskData();
                var connectionString = builder.Configuration.GetConnectionString("App") ?? "Data Source=app.db";
                __ADDRASKOUTBOX__builder.Services.AddDbContextFactory<AppDbContext>((sp, o) => o
                    .UseRaskSqlite(connectionString, o => o.StrictTables = true)
                    .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));

                // Continuous backup. Litestream streams the write-ahead log to object storage, which is what
                // makes one box a safe place to keep your only copy: if the machine dies, a fresh one restores
                // the database from the replica on startup and carries on. Durability stops depending on that
                // one disk — the whole premise of running a real product on a single server.
                //
                // Inert until you point it somewhere. To turn it on:
                //   rask deploy --env "Litestream__ReplicaUrl=s3://your-bucket/app"
                // plus whatever credentials your provider needs (e.g. AWS_ACCESS_KEY_ID / AWS_SECRET_ACCESS_KEY).
                // s3://, gcs://, abs:// and file:// replicas are all supported — see docs/sqlite.md.
                var replicaUrl = builder.Configuration["Litestream:ReplicaUrl"];
                if (!string.IsNullOrWhiteSpace(replicaUrl))
                {
                    builder.Services.AddRaskSqliteLitestream(o =>
                    {
                        o.DatabasePath = new SqliteConnectionStringBuilder(connectionString).DataSource;
                        o.ReplicaUrl = replicaUrl;
                    });
                }

                """;

            // AddRaskData takes no argument either way now. It used to need
            // `o.DispatchDomainEventsInProcess = false` whenever the outbox was on, and getting that wrong
            // silently emptied the outbox — so the framework decides it at container-build time instead, and
            // this emitter has one less way to be wrong.
            sb.Append(sqliteData.TrimStart('\n')
                    .Replace("__ADDRASKOUTBOX__", batteries.Outbox
                        ? """
                          // Transactional outbox: a domain event marked IOutboxEvent is written to the outbox table in
                          // the SAME transaction as the change that raised it, then relayed at-least-once by a
                          // background processor. Registering it is also what hands it domain-event delivery, so
                          // AddRaskData above needs no argument to match.
                          builder.Services.AddRaskOutbox<AppDbContext>();

                          """
                        : "", StringComparison.Ordinal));
        }

        if (batteries.Data)
        {
            Block(sb, """
                // Accounts: register, sign in, sign out. Backed by ASP.NET Core Identity, reached through
                // Rask's own IAuth so the same call works on the Server host, in WebAssembly and inside an
                // island. The FIRST account to register becomes the administrator; while none exists, that
                // registration needs the one-time token written to the startup log.
                builder.Services.AddRaskAuth<AppDbContext>();
                """);
        }

        if (batteries.Jobs)
        {
            Block(sb, """
                // Durable background jobs on the app's own database — no broker, no Redis. Enqueue with IJob;
                // a hosted worker polls, runs each job through its Rask.Cqrs handler, and retries with backoff.
                // Schedule recurring work here: o.AddRecurring<PurgeJob>("purge", TimeSpan.FromHours(1), () => new());
                builder.Services.AddRaskJobs<AppDbContext>();
                """);
        }

        if (batteries.Mail)
        {
            Block(sb, """
                // Transactional email queued on the app's own database and delivered off the request thread. The
                // body is a Rask component, so it uses the same component model as the UI. With no SMTP configured
                // the dev default writes each message to ./mail-pickup as an .eml file you can open — set o.Smtp
                // (host/port/credentials) to send for real.
                builder.Services.AddRaskMail<AppDbContext>(o =>
                {
                    o.From = "no-reply@example.com";
                    o.PickupDirectory = builder.Configuration["Mail:PickupDirectory"] ?? "mail-pickup";
                });
                """);
        }

        if (batteries.Cache)
        {
            Block(sb, """
                // A cache on the app's own database: the standard IDistributedCache (so ASP.NET session/output
                // caching just works) plus a typed ICache with GetOrAddAsync and absolute/sliding expiry. A
                // background purger sweeps expired rows.
                builder.Services.AddRaskCache<AppDbContext>();
                """);
        }

        if (batteries.AnySqliteOps)
        {
            Block(sb, """
                // Scheduled point-in-time backups, a second line of defence alongside the continuous replication
                // above. Taken through SQLite's Online Backup API rather than a file copy — with WAL on, copying
                // the .db can capture a torn database, because the committed data is split across the file and
                // the -wal. Same connection string, so it follows a ConnectionStrings:App override.
                builder.Services.AddRaskSqliteSnapshots(o =>
                {
                    o.DatabasePath = new SqliteConnectionStringBuilder(connectionString).DataSource;
                    o.DestinationDirectory = builder.Configuration["Sqlite:SnapshotDirectory"] ?? "snapshots";
                    o.Interval = TimeSpan.FromHours(6);
                    o.Retain = 7;
                });
                """);
        }

        if (batteries.Logs)
        {
            const string logBackupCaveat = "`rask db backup` or Litestream";
            Block(sb, """
                // The application log, kept in a database of its own so it survives the restart that hid it.
                // This registers an ILoggerProvider, so it captures exactly what every other sink sees; log
                // calls never wait on the disk (entries are buffered and written in batches), and retention
                // drops them by age and by row count.
                //
                // A SEPARATE FILE, on purpose — unlike the other database-backed batteries this one does not
                // map onto AppDbContext. Log lines arrive at machine rates, and the line you most want is the
                // one written while a transaction is failing, which on the app's context would roll back with
                // it. The trade-off: this file is NOT covered by @@LOGBACKUP@@, and log lines
                // can contain secrets — treat it as sensitive and keep it on the same persistent volume as
                // your database (`rask deploy` sets ConnectionStrings:Logs to a path on that volume).
                // Tip: an EF Core app logs every SQL command at Information, which will dominate the store
                // on the default settings. Either raise the floor for that category in Logging:LogLevel, or
                // skip it here:  o => o.ExcludedCategories.Add("Microsoft.EntityFrameworkCore.Database")
                builder.Services.AddRaskLogging(
                    builder.Configuration.GetConnectionString("Logs") ?? "Data Source=logs.db");
                """.Replace("@@LOGBACKUP@@", logBackupCaveat, StringComparison.Ordinal));
        }

        if (batteries.Push)
        {
            Block(sb, """
                // Server-sent Web Push (VAPID + RFC 8291), no external service. Generate a key pair once with
                // VapidKeys.Generate() and store it in configuration or user-secrets — the PUBLIC key is handed
                // to the browser to subscribe with; the PRIVATE key signs and must never be served.
                //
                // Registered only once a key pair is configured: AddRaskWebPush validates its options and
                // throws at startup without them, and a freshly scaffolded app has to run before you have
                // generated any keys. The subscription store is registered either way so the endpoints and
                // the UI compile and work; sending is what needs the keys.
                var vapidPublicKey = builder.Configuration["WebPush:PublicKey"];
                var vapidPrivateKey = builder.Configuration["WebPush:PrivateKey"];
                if (!string.IsNullOrWhiteSpace(vapidPublicKey) && !string.IsNullOrWhiteSpace(vapidPrivateKey))
                {
                    builder.Services.AddRaskWebPush(o =>
                    {
                        o.VapidKeys = new VapidKeys(vapidPublicKey, vapidPrivateKey);
                        o.Subject = builder.Configuration["WebPush:Subject"] ?? "mailto:admin@example.com";
                    });
                }

                builder.Services.AddSingleton<PushSubscriptionStore>();
                """);
        }

        if (batteries.Ops)
        {
            Block(sb, """
                // An operator dashboard at /_rask over every battery's table: queue depth, dead letters and the
                // errors behind them, cache contents, the log, and how this database is configured. A panel
                // only appears for a battery this app actually registered — the Logs page keeps a live tail
                // in memory, and gains a searchable History over the stored log when Rask.Logging is on.
                builder.Services.AddRaskDashboard<AppDbContext>();
                """);

            // Not conditional on the database: ServerBatteries.Normalized() sets Data whenever Ops is
            // on, because AddRaskDashboard<TContext> needs a context. So an app with the console always
            // has accounts, and always has the role to require.
            Block(sb, """
                // WHO MAY OPERATE THE APP. The dashboard shows job payloads, stored email bodies and log
                // lines, so it is gated on the ADMIN role — the one the first account to register holds.
                // Requiring merely a signed-in user would open all of that to anyone who registered,
                // which on an app with open registration is everyone.
                builder.Services.AddAuthorization(o =>
                    o.AddPolicy(RaskDashboardPolicies.Access, p => p.RequireRole(RaskRoles.Admin)));
                """);
        }
    }

    /// <summary>
    /// The <c>using</c> lines the database and battery blocks need, in the order both templates emit them.
    /// </summary>
    /// <remarks>
    /// Paired with <see cref="AppendDatabaseAndBatteries"/> — the blocks and the usings that make them
    /// compile are two halves of one decision, so they are kept beside each other rather than in the two
    /// templates that would each have to remember the other half.
    /// </remarks>
    private static string DatabaseAndBatteryUsings(ServerBatteries batteries)
    {
        var sb = new StringBuilder();
        if (batteries.Cqrs)
        {
            sb.Append("using Rask.Cqrs;\n");
        }

        if (batteries.Data)
        {
            sb.Append("using Microsoft.EntityFrameworkCore;\n");
            sb.Append("using Microsoft.EntityFrameworkCore.Diagnostics;\n");
            sb.Append("using Rask.Data;\n");
            sb.Append("using Rask.SQLite;\n");
            sb.Append("using Microsoft.Data.Sqlite;\n");
            sb.Append("using Rask.SQLite.Litestream;\n");
        }

        if (batteries.Jobs)
        {
            sb.Append("using Rask.Jobs;\n");
        }

        if (batteries.Mail)
        {
            sb.Append("using Rask.Mail;\n");
        }

        if (batteries.Cache)
        {
            sb.Append("using Rask.Cache;\n");
        }

        if (batteries.Outbox)
        {
            sb.Append("using Rask.Outbox;\n");
        }

        if (batteries.AnySqliteOps)
        {
            sb.Append("using Rask.SQLite.Snapshots;\n");
        }

        if (batteries.Logs)
        {
            sb.Append("using Rask.Logging;\n");
        }

        if (batteries.Ops)
        {
            sb.Append("using Rask.Dashboard;\n");
        }
        return sb.ToString();
    }
}
