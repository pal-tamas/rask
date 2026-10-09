using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rask.Api;
using Rask.Auth;
using Rask.Background;
using Rask.Caching;
using Rask.Core.Browser;
using Rask.Core.Forms;
using Rask.Core.Live;
using Rask.Cqrs;
using Rask.Cqrs.Server;
using Rask.Dashboard;
using Rask.Data;
using Rask.Logging;
using Rask.Mailing;
using Rask.Outbox;
using Rask.Querying;
using Rask.Server;
using Rask.SQLite.Litestream;
using Rask.SQLite.Snapshots;
using Rask.Storage;
using Rask.WebPush;

namespace Rask;

/// <summary>
/// Wires the batteries this package brings, minus the ones the app turned off.
/// </summary>
/// <remarks>
/// <para>
/// Referencing <c>Rask</c> is what turns a battery on — there is no discovery step and nothing to opt
/// into, which is why this is a plain method rather than a source generator reading the reference set.
/// The package IS the reference set.
/// </para>
/// <para>
/// Dependencies are applied downwards: turning the database off takes with it everything that cannot work
/// without one, because those all register as <c>AddRaskX&lt;TContext&gt;</c> and resolve
/// <c>IDbContextFactory&lt;TContext&gt;</c>. The log store is the exception — it owns a SQLite file of its
/// own, so it never depended on the application database in either direction.
/// </para>
/// <para>
/// Every battery reads its own <c>Rask:&lt;Area&gt;</c> section. What this adds is the defaults a battery that is on
/// by default needs in order to boot by default — a database file, a From address — and it adds them as the
/// LOWEST-precedence configuration, so appsettings.json, the environment and every <c>Configure</c> callback still win.
/// </para>
/// </remarks>
internal static class RaskBatteryWiring
{
    /// <summary>
    /// The development defaults, as configuration keys. Each one is what a fresh app needs to start before anybody has
    /// configured anything, and each is overridden by the same key set anywhere else.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string?> Defaults = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
    {
        ["Rask:ConnectionStrings:App"] = "Data Source=app.db",
        ["Rask:ConnectionStrings:Logs"] = "Data Source=logs.db",
        ["Rask:Sqlite:StrictTables"] = "true",

        // A FROM ADDRESS IS REQUIRED — MailOptions.Validate throws without one. example.com is IANA-reserved for
        // documentation, so an app that never sets this cannot accidentally send as a domain somebody owns. With no
        // SMTP configured, each message is written to ./mail-pickup as an .eml.
        ["Rask:Mail:From"] = "no-reply@example.com",
        ["Rask:Mail:PickupDirectory"] = "mail-pickup",

        // A DESTINATION IS REQUIRED — AddRaskSqliteSnapshots validates and refuses to start without one.
        ["Rask:Snapshots:DestinationDirectory"] = "snapshots",
    };

    /// <summary>
    /// The defaults that only make sense for a SQLite app: a database FILE to fall back to, and a directory to copy it
    /// into. On PostgreSQL or SQL Server a missing connection string must fail naming the key, not quietly become
    /// "Data Source=app.db" handed to a server driver — and any Rask:Snapshots value there is one the app set.
    /// </summary>
    private static readonly HashSet<string> SqliteOnlyDefaults =
        new(["Rask:ConnectionStrings:App", "Rask:Snapshots:DestinationDirectory"], StringComparer.Ordinal);

    /// <summary>The development defaults for an app on <paramref name="provider"/>.</summary>
    internal static IReadOnlyDictionary<string, string?> DefaultsFor(RaskDatabaseProvider provider) =>
        provider == RaskDatabaseProvider.Sqlite
            ? Defaults
            : Defaults.Where(d => !SqliteOnlyDefaults.Contains(d.Key)).ToDictionary(d => d.Key, d => d.Value, StringComparer.Ordinal);

    internal static void Apply(WebApplicationBuilder builder, RaskAppOptions options)
    {
        var services = builder.Services;

        var provider = ApplyConfiguration(builder, options);
        WireMediator(services, options);

        // Every scaffolded feature handler dispatches through the mediator, so a database without one has
        // nothing driving it.
        var data = options.Data.Enabled && options.Cqrs.Enabled;
        if (data)
        {
            WireData(services);
        }

        // On PostgreSQL or SQL Server the log goes into the application database (WireFor), so it is not wired here.
        var serverDatabase = data && provider != RaskDatabaseProvider.Sqlite;

        // The app's own context, when it registered one. Read here so its provider check is registered before any
        // battery: options are validated in the order they were registered, before any hosted service starts, so a
        // battery whose validation fails on the wrong database (a snapshot of a file a server connection string does
        // not name) would otherwise report first and hide the real mistake. See RaskDatabaseProviderCheck.
        var appContext = data ? FindDbContext(services) : null;
        if (appContext is not null)
        {
            AddProviderCheck(services, appContext);
        }

        WireHostBatteries(builder, options, serverDatabase);

        if (!data)
        {
            return;
        }

        WireBackups(builder, options, provider);

        // Code wins over configuration, as everywhere: a MigrateOnStart assignment beats Rask:Database:MigrateOnStart.
        var migrate = options.MigrateOnStartSet
                      ?? builder.Configuration.GetValue<bool?>(RaskDatabase.MigrateOnStartKey)
                      ?? true;
        WireDatabase(services, options, appContext, provider, migrate);
    }

    private static RaskDatabaseProvider ApplyConfiguration(WebApplicationBuilder builder, RaskAppOptions options)
    {
        // Read while services are registered, like Rask:Cqrs, because it decides which services exist: where the log is
        // kept, whether snapshots run. Read before the defaults go in, which name no provider.
        var provider = RaskDatabase.Provider(builder.Configuration);

        // Underneath every source the builder already has — appsettings.json, the environment, user secrets, the
        // command line — so each of them overrides these.
        builder.Configuration.Sources.Insert(0, new MemoryConfigurationSource { InitialData = DefaultsFor(provider) });

        // A connection string set in code beats every configuration source, the same way a Configure callback does.
        // Written into configuration rather than passed along, so everything that derives from the database — the
        // Litestream and snapshot source paths, the dashboard — reads the one value.
        if (options.ConnectionString is { } connectionString)
        {
            builder.Configuration.AddInMemoryCollection(
                [new KeyValuePair<string, string?>("Rask:ConnectionStrings:App", connectionString)]);
        }

        return provider;
    }

    private static void WireMediator(IServiceCollection services, RaskAppOptions options)
    {
        // The mediator, and the query cache that rides with it. A dispatcher without a cache means every
        // render refetches, which is the first thing anyone building over IDispatcher needs solved.
        // Validation is a property of the RENDER as much as of dispatch, so the switch is set before
        // anything else is wired: RaskValidation.AutoValidate is what a Form reads, and it is static
        // because a form has no options object to consult and exists on both hosts.
        RaskValidation.AutoValidate = options.Validation.Enabled;

        if (options.Cqrs.Enabled)
        {
            // With validation off there are no validators to run, so code turns the behavior off. With it on, the
            // behavior's default is on and Rask:Cqrs:ValidateRequests still decides: a callback that always
            // assigned would be code, and code beats configuration, so the setting would be silently dropped.
            if (options.Validation.Enabled)
            {
                services.AddRaskCqrs();
            }
            else
            {
                services.AddRaskCqrs(o => o.ValidateRequests = false);
            }
            services.AddRaskQuery();

            if (options.Validation.Enabled)
            {
                services.AddRaskRequestValidation();
            }
        }
    }

    // The session scope, the principal source and what a save tells the session — shared with a host wired by
    // hand through AddRaskData<TContext>(…), so the two cannot drift.
    private static void WireData(IServiceCollection services) => DataHostSeams.Add(services);

    private static void WireHostBatteries(WebApplicationBuilder builder, RaskAppOptions options, bool serverDatabase)
    {
        var services = builder.Services;

        if (options.Logs.Enabled && !serverDatabase)
        {
            // Its own file, deliberately: log lines arrive at machine rates, and the line you most want is
            // the one written while a transaction is failing — which on the app's context would roll back
            // with it. EF's per-command logging is excluded, or an EF app's log is mostly its own SQL.
            services.AddRaskLogging(o =>
            {
                o.ExcludedCategories.Add("Microsoft.EntityFrameworkCore.Database");
                options.Logs.Apply(o);
            });
        }

        // Web Push needs a VAPID key pair, and AddRaskWebPush validates its options and refuses to start without
        // one. A freshly scaffolded app has to run before anybody has generated any keys, so this is wired when the
        // keys exist rather than refusing to start when they do not.
        if (options.Push.Enabled && HasVapidKeys(builder.Configuration, options))
        {
            services.AddRaskWebPush(o => options.Push.Apply(o));
        }

        // The PWA battery serves the manifest and, more importantly, the service worker at
        // {PathBase}/rask-sw.js, registered by the page's head. MDN's PushManager is reached only through a
        // registered worker (`await Navigator.ServiceWorker.Ready`), so without this call a push subscription
        // waits forever. The default manifest is named after the entry
        // assembly so an app that configures nothing is still installable.
        if (options.Pwa.Enabled)
        {
            var manifest = DefaultManifest(builder.Environment);
            options.Pwa.Apply(manifest);
            services.AddRaskPwa(manifest);
        }

        // Above the early return, because HTTP endpoints have nothing to do with the database — an app
        // with no DbContext still has an API, and wiring this alongside the pillars would silently give
        // it none.
        if (options.Api.Enabled)
        {
            services.AddRaskApi(o => options.Api.Apply(o));
        }

        // The ASYNC half for HTTP endpoints, which the platform cannot supply: MVC's ModelState and
        // Validator.TryValidateObject are both synchronous, so the discovered AbstractValidator<T> — and
        // above all a MustAsync rule inside it — has nowhere to run on a controller action or a minimal
        // API. Registered off the validation battery rather than the API one: a minimal API is an
        // endpoint whether or not this app maps controllers, and "validation is on" has to mean the same
        // thing at every seam.
        if (options.Validation.Enabled)
        {
            services.AddRaskApiValidation();
        }
    }

    private static void WireBackups(WebApplicationBuilder builder, RaskAppOptions options, RaskDatabaseProvider provider)
    {
        var services = builder.Services;

        if (provider == RaskDatabaseProvider.Sqlite)
        {
            // Continuous backup, inert until a replica is configured. It is what makes one box a safe place to
            // keep your only copy: if the machine dies, a fresh one restores from the replica and carries on. The
            // database it replicates defaults to the file behind Rask:ConnectionStrings:App.
            if (!string.IsNullOrWhiteSpace(builder.Configuration["Rask:Litestream:ReplicaUrl"]))
            {
                services.AddRaskSqliteLitestream();

                // And the other half of the promise: a fresh box pulls the database back from the replica
                // before anything opens it. On a box that already has app.db this is a no-op. An app that
                // set its own pre-open step keeps it.
                options.RunBeforeDatabaseOpens ??= static async sp =>
                    await sp.RestoreSqliteFromLitestream().ConfigureAwait(false);
            }

            if (options.Snapshots.Enabled)
            {
                // A second line of defence beside the continuous replication. Taken through SQLite's Online
                // Backup API rather than a file copy: with WAL on, copying the .db can capture a torn database
                // because the committed data is split across the file and the -wal.
                services.AddRaskSqliteSnapshots(o => options.Snapshots.Apply(o));
            }
        }
        else
        {
            RefuseSqliteOnlyBatteries(builder.Configuration, options, provider);
        }
    }

    private static void WireDatabase(
        IServiceCollection services, RaskAppOptions options, Type? appContext, RaskDatabaseProvider provider, bool migrate)
    {
        // The pillars need the application's DbContext as a type argument. The app already named it, in
        // its own AddDbContextFactory call — and because this runs last, that registration is sitting in
        // the collection. Reading it there beats asking for the name a second time.
        // The read faces are queried through a context of their own — two entity types cannot map one
        // table, and an aggregate has no business being reachable from a query surface with no borders. It
        // is pointed at the same database by the same setting, so there is still one place that says where
        // the data is. An app whose read side is somewhere else entirely says so with ReadDb.Configure.
        //
        // Through a factory of its own rather than AddDbContextFactory, so EF's tooling never sees it. `dotnet ef`
        // lists every DbContextOptions<T> in the container and refuses to guess between two — and this one has no
        // migrations to add, ever: it reads the tables the write side owns. With it hidden, `rask db add` finds
        // exactly one context, Rask's or the app's, and needs no --context.
        services.TryAddSingleton<IDbContextFactory<RaskReadDbContext>>(static sp => new ReadContextFactory(sp));

        if (appContext is not null)
        {
            WireContextBatteries(services, options, appContext, provider, migrate);
        }
        else
        {
            // No context of its own, so the app gets Rask's — mapped from the entities the source
            // generator found, which is what lets an app declare an entity and nothing else and still
            // have a database. Registered unconditionally rather than only when an entity exists: the
            // generated registry is populated by a module initializer, and an entity living in a class
            // library the app has not yet touched would make "are there entities?" answer differently
            // depending on what ran first.
            //
            // Its migrations live in the APP: this context's assembly is Rask itself, where EF would otherwise
            // look for them and find none, on every `rask db update` and at every start.
            var app = options.AppAssembly;
            services.AddDbContextFactory<RaskAppDbContext>((sp, o) => o
                .UseRaskDatabase(sp)
                .MigrationsIn(app)
                .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));

            WireContextBatteries(services, options, typeof(RaskAppDbContext), provider, migrate);
        }
    }

    /// <summary>
    /// Points the relational provider at <paramref name="assembly"/> for migrations — whichever provider
    /// <c>UseRaskDatabase</c> chose, without naming it.
    /// </summary>
    /// <remarks>
    /// The provider's extension derives from <see cref="RelationalOptionsExtension"/>, and the options keep
    /// extensions by their exact type, so the replacement has to be added under the provider's own type: the
    /// generic <c>AddOrUpdateExtension&lt;RelationalOptionsExtension&gt;</c> would file a SECOND relational
    /// extension beside the provider's and EF would refuse the options as naming two providers.
    /// </remarks>
    [UnconditionalSuppressMessage("Trimming", "IL2060",
        Justification = "The extension type is the provider's, rooted by the UseRaskX call that created it.")]
    private static DbContextOptionsBuilder MigrationsIn(this DbContextOptionsBuilder builder, Assembly? assembly)
    {
        if (assembly?.GetName().Name is not { } name
            || builder.Options.Extensions.OfType<RelationalOptionsExtension>().FirstOrDefault() is not { } relational)
        {
            return builder;
        }

        var updated = relational.WithMigrationsAssembly(name);
        typeof(IDbContextOptionsBuilderInfrastructure)
            .GetMethod(nameof(IDbContextOptionsBuilderInfrastructure.AddOrUpdateExtension))!
            .MakeGenericMethod(updated.GetType())
            .Invoke(builder, [updated]);

        return builder;
    }

    /// <summary>
    /// The read context, built the way <c>AddDbContextFactory</c> would build it, minus the
    /// <c>DbContextOptions&lt;RaskReadDbContext&gt;</c> registration that EF's tooling enumerates.
    /// </summary>
    private sealed class ReadContextFactory(IServiceProvider services) : IDbContextFactory<RaskReadDbContext>
    {
        private readonly Lazy<DbContextOptions<RaskReadDbContext>> _options = new(() =>
            new DbContextOptionsBuilder<RaskReadDbContext>()
                .UseApplicationServiceProvider(services)
                .UseRaskDatabase(services)
                .Options);

        [UnconditionalSuppressMessage("Trimming", "IL2026",
            Justification = "The read context's constructor carries EF Core's own [RequiresUnreferencedCode]; this "
                            + "package is the server host, which is never trimmed.")]
        public RaskReadDbContext CreateDbContext() => new(_options.Value);
    }

    /// <summary>
    /// On PostgreSQL or SQL Server: leaves out the SQLite-only batteries the app merely defaulted, and refuses the ones
    /// it asked for.
    /// </summary>
    /// <remarks>
    /// Snapshots and Litestream both copy a SQLite FILE, and a server database has none. A battery that is on only
    /// because every battery is on by default is left out without a word — a Postgres app should not have to say
    /// <c>Snapshots.Off()</c>. One the app configured, though, is a backup it believes it has, and quietly never running
    /// it would be the worst answer, so that refuses the start and names what to remove.
    /// </remarks>
    private static void RefuseSqliteOnlyBatteries(
        ConfigurationManager configuration,
        RaskAppOptions options,
        RaskDatabaseProvider provider)
    {
        var name = RaskDatabase.Name(provider);

        if (!string.IsNullOrWhiteSpace(configuration["Rask:Litestream:ReplicaUrl"]))
        {
            throw new InvalidOperationException(
                $"Rask:Litestream:ReplicaUrl is set, but {RaskDatabase.ProviderKey} is {name}. Litestream replicates a "
                + "SQLite file, and this app has none: back the database up with its own tools, and remove "
                + "Rask:Litestream:ReplicaUrl (Rask__Litestream__ReplicaUrl in the environment).");
        }

        if (!options.Snapshots.Enabled)
        {
            return;
        }

        // Only the app's own sources can hold a Rask:Snapshots value here: its default is not added off SQLite.
        var configured = configuration.GetSection("Rask:Snapshots").AsEnumerable()
            .Any(setting => !string.IsNullOrWhiteSpace(setting.Value));

        if (configured || options.Snapshots.IsConfigured || options.Snapshots.TurnedOn)
        {
            throw new InvalidOperationException(
                $"Snapshots are configured, but {RaskDatabase.ProviderKey} is {name}. A snapshot copies the SQLite file, "
                + "and this app has none. Remove the Rask:Snapshots section and any c.Snapshots.Configure or "
                + "c.Snapshots.On call, or turn the battery off with app.Configure(c => c.Snapshots.Off()).");
        }
    }

    /// <summary>
    /// The application's <c>DbContext</c>, read off its own <c>IDbContextFactory&lt;T&gt;</c> registration.
    /// </summary>
    /// <remarks>
    /// Null when the app registered no factory — in which case it has no database and the pillars that
    /// need one are simply not wired. With several, the first wins; an app with two databases is past the
    /// point where a convention should be guessing, and can name the one it means by calling the
    /// <c>AddRaskX&lt;TContext&gt;</c> methods itself.
    /// </remarks>
    private static Type? FindDbContext(IServiceCollection services)
    {
        foreach (var type in services.Select(static descriptor => descriptor.ServiceType))
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IDbContextFactory<>))
            {
                return type.GetGenericArguments()[0];
            }
        }

        return null;
    }

    // One reflection point, closing a generic method over the context type discovered above. The type is
    // rooted by the app's own AddDbContextFactory<T> call, so it is never trimmed away.
    [UnconditionalSuppressMessage("Trimming", "IL2060",
        Justification = "TContext comes from the app's own IDbContextFactory<T> registration, which roots it.")]
    private static void WireContextBatteries(
        IServiceCollection services,
        RaskAppOptions options,
        Type context,
        RaskDatabaseProvider provider,
        bool migrate) =>
        typeof(RaskBatteryWiring)
#pragma warning disable S3011 // this class's own private generic step, closed over the app's context type
            .GetMethod(nameof(WireFor), BindingFlags.NonPublic | BindingFlags.Static)!
#pragma warning restore S3011
            .MakeGenericMethod(context)
            .Invoke(null, [services, options, provider, migrate]);

    // The same reflection point as WireContextBatteries, over the same app-rooted context type.
    [UnconditionalSuppressMessage("Trimming", "IL2060",
        Justification = "The context type comes from the app's own IDbContextFactory<T> registration, which roots it.")]
    private static void AddProviderCheck(IServiceCollection services, Type context) =>
        typeof(RaskBatteryWiring)
#pragma warning disable S3011 // this class's own private generic step, closed over the app's context type
            .GetMethod(nameof(AddProviderCheckFor), BindingFlags.NonPublic | BindingFlags.Static)!
#pragma warning restore S3011
            .MakeGenericMethod(context)
            .Invoke(null, [services]);

    // A start-up validation rather than a hosted service, because validation runs before every hosted service — and
    // in registration order, which Apply keeps ahead of every battery. The check throws its own message; returning
    // false would only offer a fixed one, and the useful message names both providers.
    private static void AddProviderCheckFor<TContext>(IServiceCollection services)
        where TContext : DbContext =>
        services.AddOptions<RaskDatabaseProviderCheck<TContext>>()
            .Validate<IDbContextFactory<TContext>, IConfiguration>(
                static (check, contexts, configuration) => check.Verify(contexts, configuration))
            .ValidateOnStart();

    // Unless the app wired a log store itself, which wins here as it does for every battery. Calling
    // AddRaskLogging<TContext> anyway would register its model check for a table nothing writes, and fail the boot.
    private static void WireLogStore<TContext>(
        IServiceCollection services, RaskAppOptions options, RaskDatabaseProvider provider)
        where TContext : DbContext
    {
        if (options.Logs.Enabled && provider != RaskDatabaseProvider.Sqlite
            && !services.Any(static d => d.ServiceType == typeof(ILogs)))
        {
            // On PostgreSQL or SQL Server the log goes into the application database. The reasons it keeps a file of its
            // own on SQLite weigh differently there: the server locks rows rather than the whole database, and every
            // flush runs on a context and connection of its own, so a line logged inside a failing transaction still
            // survives the rollback. EF's per-command logging is excluded for the same reason as on SQLite.
            services.AddRaskLogging<TContext>(o =>
            {
                o.ExcludedCategories.Add("Microsoft.EntityFrameworkCore.Database");
                options.Logs.Apply(o);
            });
        }
    }

    private static void WireFor<TContext>(
        IServiceCollection services,
        RaskAppOptions options,
        RaskDatabaseProvider provider,
        bool migrate)
        where TContext : DbContext
    {
        // Bind the model surface to this context, so `Product.Where(…)` reaches it without anything being
        // injected. AddRaskData is idempotent,
        // so this only adds the binding.
        services.AddRaskData<TContext>();

        // The app's pending migrations, applied before any worker below starts — see MigrateOnStart.
        if (migrate)
        {
            services.AddHostedService<MigrateOnStart<TContext>>();
        }

        WireLogStore<TContext>(services, options, provider);

        // The outbox follows Data: a handler chooses durability, so it is on whenever there is a database to write to.
        services.AddRaskOutbox<TContext>(o => options.Outbox.Apply(o));

        if (options.Jobs.Enabled)
        {
            services.AddRaskJobs<TContext>(o => options.Jobs.Apply(o));
        }

        // Accounts. Wired here rather than beside the host because it needs the application context —
        // Identity's stores live on it — and because AddRaskAuth registers the cookie scheme, which
        // RaskApp then picks up: it calls UseAuthentication/UseAuthorization before MapRask whenever a
        // scheme provider is present, so an app never has to order that middleware itself. That is the
        // mistake RASK024 exists to catch, and "auth is on by default" would otherwise reintroduce it.
        if (options.Auth.Enabled)
        {
            services.AddRaskAuth<TContext>(o => options.Auth.Apply(o));
        }

        if (options.Mail.Enabled)
        {
            services.AddRaskMail<TContext>(o => options.Mail.Apply(o));
        }

        if (options.Cache.Enabled)
        {
            services.AddRaskCache<TContext>(o => options.Cache.Apply(o));
        }

        WireStorageAndPush<TContext>(services, options);
        WireOps<TContext>(services, options);
    }

    private static void WireStorageAndPush<TContext>(IServiceCollection services, RaskAppOptions options)
        where TContext : DbContext
    {
        if (options.Storage.Enabled)
        {
            // Uploaded files, kept by id: the bytes on disk (/data/files on the deploy volume) or in a bucket, and a
            // StoredFile row on this context. The Rask__Storage__* keys are read inside AddRaskStorage itself rather than
            // here, so an app wired by hand in Program.cs honours exactly the same configuration as this one.
            services.AddRaskStorage<TContext>(o => options.Storage.Apply(o));
        }

        if (options.Push.Enabled)
        {
            // The browsers that subscribed, on this context, and Push.Send(message) to reach them. Wired with or
            // without a key pair — the table and the subscribe endpoints work first, and sending names the missing
            // keys. When a pair IS configured, the sender was already registered above with start-time validation.
            services.AddRaskWebPush<TContext>(o => options.Push.Apply(o));
        }
    }

    private static void WireOps<TContext>(IServiceCollection services, RaskAppOptions options)
        where TContext : DbContext
    {
        if (options.Ops.Enabled)
        {
            // WHO MAY OPERATE THE APP. The dashboard shows job payloads, stored email bodies and log lines, so
            // with accounts on it is gated on the administrator — the role the first account to register holds.
            // Merely signed-in would open all of that to anyone who registered, which on an app with open
            // registration is everyone. An app that names the policy itself is left alone, which is why this is
            // a PostConfigure that fills a gap rather than a Configure that would run after the app's and win.
            if (options.Auth.Enabled)
            {
                services.PostConfigure<AuthorizationOptions>(static authz =>
                {
                    if (authz.GetPolicy(RaskDashboardPolicies.Access) is null)
                    {
                        authz.AddPolicy(RaskDashboardPolicies.Access, policy => policy.RequireRole(RaskRoles.Admin));
                    }
                });
            }

            // Configured through Rask:Ops.
            services.AddRaskDashboard<TContext>();
        }
    }

    // A manifest an app gets without asking: named after the app, standalone display, Rask's own icon.
    // Name is `required`, so there is no such thing as a manifest with nothing filled in — the question
    // is only whether the default is the app's name or a placeholder, and the app's name is always better.
    // An app with a wwwroot/icon.svg — every scaffolded one — installs with that icon rather than none.
    private static WebAppManifest DefaultManifest(IWebHostEnvironment environment) => new()
    {
        Name = environment.ApplicationName,
        ShortName = environment.ApplicationName,
        Display = DisplayMode.Standalone,
        Icons = environment.WebRootFileProvider?.GetFileInfo("icon.svg").Exists == true
            ? [new ManifestIcon("icon.svg", "any", "image/svg+xml", "any maskable")]
            : [],
    };

    // Web Push is configured either through the block or through Rask:Push:VapidKeys; either is enough, and
    // AddRaskWebPush binds the section itself.
    private static bool HasVapidKeys(ConfigurationManager configuration, RaskAppOptions options)
    {
        var probe = new PushOptions();
        options.Push.Apply(probe);
        if (probe.VapidKeys is not null)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(configuration["Rask:Push:VapidKeys:PublicKey"])
               && !string.IsNullOrWhiteSpace(configuration["Rask:Push:VapidKeys:PrivateKey"]);
    }
}
