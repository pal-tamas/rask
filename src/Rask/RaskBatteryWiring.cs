using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rask.Api;
using Rask.Auth;
using Rask.Cache;
using Rask.Core.Browser;
using Rask.Cqrs;
using Rask.Dashboard;
using Rask.Data;
using Rask.Jobs;
using Rask.Logging;
using Rask.Mail;
using Rask.Outbox;
using Rask.Query;
using Rask.Server;
using Rask.SQLite.Litestream;
using Rask.SQLite.Snapshots;
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
/// </remarks>
internal static class RaskBatteryWiring
{
    internal static void Apply(WebApplicationBuilder builder, RaskAppOptions options)
    {
        var services = builder.Services;

        // The mediator, and the query cache that rides with it. A dispatcher without a cache means every
        // render refetches, which is the first thing anyone building over IDispatcher needs solved.
        if (options.Cqrs.Enabled)
        {
            services.AddRaskCqrs();
            services.AddRaskQuery();
        }

        // Every scaffolded feature handler dispatches through the mediator, so a database without one has
        // nothing driving it.
        var data = options.Data.Enabled && options.Cqrs.Enabled;
        if (data)
        {
            services.AddRaskData();
        }

        if (options.Logs.Enabled)
        {
            // Its own file, deliberately: log lines arrive at machine rates, and the line you most want is
            // the one written while a transaction is failing — which on the app's context would roll back
            // with it. EF's per-command logging is excluded, or an EF app's log is mostly its own SQL.
            var logOptions = new RaskLoggingOptions();
            options.Logs.Apply(logOptions);
            services.AddRaskLogging(
                builder.Configuration.GetConnectionString("Logs") ?? "Data Source=logs.db",
                o =>
                {
                    o.ExcludedCategories.Add("Microsoft.EntityFrameworkCore.Database");
                    options.Logs.Apply(o);
                });
        }

        // Web Push needs a VAPID key pair, and AddRaskWebPush validates its options and throws without
        // one. A freshly scaffolded app has to run before anybody has generated any keys, so this is wired
        // when the keys exist rather than refusing to start when they do not.
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

        if (!data)
        {
            return;
        }

        var connectionString = options.ConnectionString
                               ?? builder.Configuration.GetConnectionString("App")
                               ?? "Data Source=app.db";

        // Continuous backup, inert until a replica is configured. It is what makes one box a safe place to
        // keep your only copy: if the machine dies, a fresh one restores from the replica and carries on.
        var replicaUrl = builder.Configuration["Litestream:ReplicaUrl"];
        if (!string.IsNullOrWhiteSpace(replicaUrl))
        {
            services.AddRaskSqliteLitestream(o =>
            {
                o.DatabasePath = DataSourceOf(connectionString);
                o.ReplicaUrl = replicaUrl;
            });
        }

        if (options.Snapshots.Enabled)
        {
            // A second line of defence beside the continuous replication. Taken through SQLite's Online
            // Backup API rather than a file copy: with WAL on, copying the .db can capture a torn database
            // because the committed data is split across the file and the -wal.
            services.AddRaskSqliteSnapshots(o =>
            {
                o.DatabasePath = DataSourceOf(connectionString);

                // A DESTINATION IS REQUIRED — AddRaskSqliteSnapshots validates and throws without one. A
                // battery that is on by default has to boot by default, so it gets a working directory
                // rather than a demand; the app overrides it below, or through configuration.
                o.DestinationDirectory = builder.Configuration["Sqlite:SnapshotDirectory"] ?? "snapshots";
                options.Snapshots.Apply(o);
            });
        }

        // The pillars need the application's DbContext as a type argument. The app already named it, in
        // its own AddDbContextFactory call — and because this runs last, that registration is sitting in
        // the collection. Reading it there beats asking for the name a second time.
        if (FindDbContext(services) is { } context)
        {
            WireContextBatteries(services, options, context);
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
    private static void WireContextBatteries(IServiceCollection services, RaskAppOptions options, Type context) =>
        typeof(RaskBatteryWiring)
            .GetMethod(nameof(WireFor), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(context)
            .Invoke(null, [services, options]);

    private static void WireFor<TContext>(IServiceCollection services, RaskAppOptions options)
        where TContext : DbContext
    {
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
            services.AddRaskMail<TContext>(o =>
            {
                // A FROM ADDRESS IS REQUIRED — MailOptions.Validate throws without one, and a battery that
                // is on by default has to boot by default. example.com is IANA-reserved for documentation,
                // so an app that never sets this cannot accidentally send as a domain somebody owns. With
                // no SMTP configured the dev default writes each message to ./mail-pickup as an .eml.
                o.From = "no-reply@example.com";
                o.PickupDirectory = "mail-pickup";
                options.Mail.Apply(o);
            });
        }

        if (options.Cache.Enabled)
        {
            services.AddRaskCache<TContext>(o => options.Cache.Apply(o));
        }

        if (options.Ops.Enabled)
        {
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

    // Web Push is configured either through the block or straight from configuration; either is enough.
    private static bool HasVapidKeys(IConfiguration configuration, RaskAppOptions options)
    {
        var probe = new WebPushOptions();
        options.Push.Apply(probe);
        if (probe.VapidKeys is not null)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(configuration["WebPush:PublicKey"])
               && !string.IsNullOrWhiteSpace(configuration["WebPush:PrivateKey"]);
    }

    // The file path out of a connection string, for the backup paths that need the file rather than the
    // string. Kept here rather than referencing Microsoft.Data.Sqlite's builder for one property.
    private static string DataSourceOf(string connectionString)
    {
        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length == 2 && pair[0].Trim().Equals("Data Source", StringComparison.OrdinalIgnoreCase))
            {
                return pair[1].Trim();
            }
        }

        return connectionString;
    }
}
