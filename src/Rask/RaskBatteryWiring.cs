using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Rask.Api;
using Rask.Auth;
using Rask.Cache;
using Rask.Core.Browser;
using Rask.Core.Forms;
using Rask.Cqrs;
using Rask.Dashboard;
using Rask.Data;
using Rask.Jobs;
using Rask.Logging;
using Rask.Mail;
using Rask.Outbox;
using Rask.Query;
using Rask.Server;
using Rask.SQLite;
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
    internal static readonly IReadOnlyDictionary<string, string?> Defaults = new Dictionary<string, string?>
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

        // Every scaffolded feature handler dispatches through the mediator, so a database without one has
        // nothing driving it.
        var data = options.Data.Enabled && options.Cqrs.Enabled;
        if (data)
        {
            services.AddRaskData();
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
        // {PathBase}/rask-sw.js. AddRask() registers IWebPush/INotifications/IBadge/IWakeLock
        // unconditionally, and on a Server host their JS helper is served only by this call — so without
        // it those four inject fine and then fail on a 404. The default manifest is named after the entry
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

        if (!data)
        {
            return;
        }

        if (provider == RaskDatabaseProvider.Sqlite)
        {
            // Continuous backup, inert until a replica is configured. It is what makes one box a safe place to
            // keep your only copy: if the machine dies, a fresh one restores from the replica and carries on. The
            // database it replicates defaults to the file behind Rask:ConnectionStrings:App.
            if (!string.IsNullOrWhiteSpace(builder.Configuration["Rask:Litestream:ReplicaUrl"]))
            {
                services.AddRaskSqliteLitestream();
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

        // The pillars need the application's DbContext as a type argument. The app already named it, in
        // its own AddDbContextFactory call — and because this runs last, that registration is sitting in
        // the collection. Reading it there beats asking for the name a second time.
        if (appContext is not null)
        {
            WireContextBatteries(services, options, appContext, provider);
        }
        else
        {
            // No context of its own, so the app gets Rask's — mapped from the entities the source
            // generator found, which is what lets an app declare an entity and nothing else and still
            // have a database. Registered unconditionally rather than only when an entity exists: the
            // generated registry is populated by a module initializer, and an entity living in a class
            // library the app has not yet touched would make "are there entities?" answer differently
            // depending on what ran first.
            services.AddDbContextFactory<RaskAppDbContext>((sp, o) => o
                .UseRaskDatabase(sp)
                .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));

            WireContextBatteries(services, options, typeof(RaskAppDbContext), provider);
        }
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
        IConfiguration configuration,
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
        foreach (var descriptor in services)
        {
            var type = descriptor.ServiceType;
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
        RaskDatabaseProvider provider) =>
        typeof(RaskBatteryWiring)
            .GetMethod(nameof(WireFor), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(context)
            .Invoke(null, [services, options, provider]);

    // The same reflection point as WireContextBatteries, over the same app-rooted context type.
    [UnconditionalSuppressMessage("Trimming", "IL2060",
        Justification = "The context type comes from the app's own IDbContextFactory<T> registration, which roots it.")]
    private static void AddProviderCheck(IServiceCollection services, Type context) =>
        typeof(RaskBatteryWiring)
            .GetMethod(nameof(AddProviderCheckFor), BindingFlags.NonPublic | BindingFlags.Static)!
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

    private static void WireFor<TContext>(
        IServiceCollection services,
        RaskAppOptions options,
        RaskDatabaseProvider provider)
        where TContext : DbContext
    {
        // Bind the ambient database to this context, so `Product.Where(…)` and `Db.Begin()` reach it
        // without anything being injected. AddRaskData is idempotent, so this only adds the binding.
        services.AddRaskData<TContext>();

        // Unless the app wired a log store itself, which wins here as it does for every battery. Calling
        // AddRaskLogging<TContext> anyway would register its model check for a table nothing writes, and fail the boot.
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

        // The outbox first, so a reader meets durable delivery before the things that use it. Order is not
        // load-bearing — see OutboxDeliveryHandoverTests, which pins that both ways round work.
        if (options.Outbox.Enabled)
        {
            services.AddRaskOutbox<TContext>(o => options.Outbox.Apply(o));
        }

        if (options.Jobs.Enabled)
        {
            services.AddRaskJobs<TContext>(o => options.Jobs.Apply(o));
        }

        // Accounts. Wired here rather than beside the host because it needs the application context —
        // Identity's stores live on it — and because AddRaskAuth registers the cookie scheme, which
        // RaskApp then picks up: it calls UseAuthentication/UseAuthorization before UseRask whenever a
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

        if (options.Storage.Enabled)
        {
            // Uploaded files, kept by id: the bytes on disk (/data/files on the deploy volume) or in a bucket, and a
            // StoredFile row on this context. The Storage__* keys are read inside AddRaskStorage itself rather than
            // here, so an app wired by hand in Program.cs honours exactly the same configuration as this one.
            services.AddRaskStorage<TContext>(o => options.Storage.Apply(o));
        }

        if (options.Ops.Enabled)
        {
            // Configured through Rask:Dashboard.
            services.AddRaskDashboard<TContext>();
        }
    }

    // A manifest an app gets without asking: named after the app, standalone display, Rask's own icon.
    // Name is `required`, so there is no such thing as a manifest with nothing filled in — the question
    // is only whether the default is the app's name or a placeholder, and the app's name is always better.
    private static WebAppManifest DefaultManifest(IWebHostEnvironment environment) => new()
    {
        Name = environment.ApplicationName,
        ShortName = environment.ApplicationName,
        Display = DisplayMode.Standalone,
    };

    // Web Push is configured either through the block or through Rask:WebPush:VapidKeys; either is enough, and
    // AddRaskWebPush binds the section itself.
    private static bool HasVapidKeys(IConfiguration configuration, RaskAppOptions options)
    {
        var probe = new WebPushOptions();
        options.Push.Apply(probe);
        if (probe.VapidKeys is not null)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(configuration["Rask:WebPush:VapidKeys:PublicKey"])
               && !string.IsNullOrWhiteSpace(configuration["Rask:WebPush:VapidKeys:PrivateKey"]);
    }
}
