using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Rask.Hosting.Shared;

namespace Rask.Meta.Hosting;

/// <summary>
///     Registers the supervised Node process and the forwarding machinery in front of it.
/// </summary>
public static class RaskMetaServiceCollectionExtensions
{
    /// <summary>
    ///     Where the framework's own dev server is listening, during a <c>rask dev</c> session. Set by the
    ///     CLI; unset in every deployed app.
    /// </summary>
    /// <remarks>
    ///     An environment variable rather than an option because the app's own code cannot know it — the
    ///     port belongs to a process the CLI started beside this one. Same channel and same shape as
    ///     <c>RASK_ISLANDS_DEV</c> on the islands lane.
    /// </remarks>
    internal const string DevServerVariable = "RASK_META_DEV";

    /// <summary>
    ///     Adds hosting for a meta framework front end running as a supervised Node process.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Registration is where the options live, rather than at
    ///         <see cref="RaskMetaEndpointExtensions.UseRaskMeta" />, because the supervisor needs them
    ///         before the pipeline is built — it has to start the process and wait for it to listen while
    ///         the app is still coming up.
    ///     </para>
    ///     <para>
    ///         Precedence, lowest first: what the build baked into the assembly, the <c>Rask:Meta</c>
    ///         configuration section (<c>Framework</c> by name — <c>Nuxt</c>, <c>Next</c>, …), <paramref name="configure" />,
    ///         and finally a <c>rask dev</c> session's dev server. Idempotent: the first call's options win.
    ///     </para>
    /// </remarks>
    /// <param name="services">The app's service collection.</param>
    /// <param name="configure">Adjusts <see cref="MetaHostingOptions" />, after the <c>Rask:Meta</c> section.</param>
    /// <returns><paramref name="services" />, for chaining.</returns>
    public static IServiceCollection AddRaskMeta(
        this IServiceCollection services,
        Action<MetaHostingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // What the build baked first, then Rask:Meta, then whatever the app says. That order is the contract:
        // naming the framework in the .csproj is the ordinary way, because the build needs it there anyway to
        // know what to publish — and configuration and configure() both stay able to override, for a framework
        // this package has no preset for, or an app that resolves its front end some other way.
        var registered = services.AddRaskOptions<MetaHostingOptions>(
            "Rask:Meta",
            BindSection,
            configure,
            validate: null,
            defaults: static o => MetaMetadata.Apply(o, Assembly.GetEntryAssembly()));

        if (registered)
        {
            // PostConfigure, so a dev session's port lands after the section and every callback.
            services.AddOptions<MetaHostingOptions>()
                .PostConfigure(static o => ApplyDevServer(o, Environment.GetEnvironmentVariable));
        }

        services.TryAddSingleton<MetaPaths>();
        services.TryAddSingleton<NodeReadiness>();
        services.TryAddSingleton<MetaDrain>();
        services.AddHttpForwarder();
        services.TryAddSingleton<NodeForwarder>();

        // AddHostedService, which is TryAddEnumerable underneath, rather than a plain AddSingleton —
        // that appends unconditionally, so calling AddRaskMeta() twice (an app plus a library, or a
        // duplicated line) would start TWO supervisors racing for the same port. The second loses with
        // EADDRINUSE, restarts until its budget is spent, and takes the host down with it.
        services.AddHostedService<NodeSupervisor>();

        return services;
    }

    /// <summary>Reads the <c>Rask:Meta</c> section onto the options, key by key.</summary>
    /// <remarks>
    ///     Key by key rather than one <c>Bind</c>, because <see cref="MetaHostingOptions.Framework" /> is a preset
    ///     object with required members, which the binder cannot construct. Configuration names the preset instead
    ///     — the same names the build's <c>RaskMetaFramework</c> property uses. Every other value is converted by the
    ///     binding source generator; <c>RaskMetaOptionsBindingTests</c> pins that each settable property is read.
    /// </remarks>
    internal static void BindSection(IConfigurationSection section, MetaHostingOptions options)
    {
        if (section[nameof(MetaHostingOptions.Framework)] is { Length: > 0 } name)
        {
            options.Framework = MetaFramework.ByName(name)
                                ?? throw new InvalidOperationException(
                                    $"Framework '{name}' is not a meta framework Rask hosts.");
        }

        options.AppDirectory = section.GetValue(nameof(MetaHostingOptions.AppDirectory), options.AppDirectory)!;
        options.NodeExecutable = section.GetValue(nameof(MetaHostingOptions.NodeExecutable), options.NodeExecutable)!;
        options.Port = section.GetValue(nameof(MetaHostingOptions.Port), options.Port);
        options.StartupTimeout = section.GetValue(nameof(MetaHostingOptions.StartupTimeout), options.StartupTimeout);
        options.ShutdownTimeout = section.GetValue(nameof(MetaHostingOptions.ShutdownTimeout), options.ShutdownTimeout);
        options.MaxRestartAttempts =
            section.GetValue(nameof(MetaHostingOptions.MaxRestartAttempts), options.MaxRestartAttempts);
        options.HealthyRunThreshold =
            section.GetValue(nameof(MetaHostingOptions.HealthyRunThreshold), options.HealthyRunThreshold);
        options.BaseUrl = section.GetValue(nameof(MetaHostingOptions.BaseUrl), options.BaseUrl);
        options.BaseUrlVariable =
            section.GetValue(nameof(MetaHostingOptions.BaseUrlVariable), options.BaseUrlVariable)!;
        options.SuperviseNode = section.GetValue(nameof(MetaHostingOptions.SuperviseNode), options.SuperviseNode);

        foreach (var variable in section.GetSection(nameof(MetaHostingOptions.Environment)).GetChildren())
        {
            if (variable.Value is { } value)
            {
                options.Environment[variable.Key] = value;
            }
        }
    }

    /// <summary>
    ///     Points the host at a front end that is already running, when <c>rask dev</c> says one is.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A dev session has no built front end at all — <c>rask dev</c> passes
    ///         <c>RaskMetaBuild=false</c> precisely so a full production build of Nuxt or Next does not
    ///         run on every save — so the supervisor would refuse to start and take the host with it.
    ///         What is running instead is the framework's own dev server, which is exactly the case
    ///         <see cref="MetaHostingOptions.SuperviseNode" /> already describes: someone else is running
    ///         the front end. This just supplies the other half of that answer, the port it is on.
    ///     </para>
    ///     <para>
    ///         The result is that both addresses work during development. The dev server's own port is
    ///         where HMR is native and where the browser is opened; this host's port still renders pages,
    ///         by forwarding, so a link to <c>:5000</c> is not a dead end.
    ///     </para>
    ///     <para>
    ///         Applied AFTER <c>configure</c> and the <c>Rask:Meta</c> section, which is the one place the
    ///         ordinary precedence is inverted. An app that pins <c>Port</c> for production would otherwise
    ///         silently defeat every dev session on a framework whose dev server listens somewhere else — and
    ///         this variable is set by the dev tool for the life of one session, not configuration anyone deploys.
    ///     </para>
    /// </remarks>
    internal static void ApplyDevServer(MetaHostingOptions options, Func<string, string?> readEnv)
    {
        var value = readEnv(DevServerVariable);
        if (string.IsNullOrWhiteSpace(value) || !TryReadPort(value, out var port))
        {
            return;
        }

        options.SuperviseNode = false;
        options.Port = port;
    }

    /// <summary>A dev server URL (<c>http://localhost:3000</c>) or a bare port.</summary>
    private static bool TryReadPort(string value, out int port)
    {
        if (int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out port))
        {
            return port is > 0 and <= 65535;
        }

        if (Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) && uri.Port > 0)
        {
            port = uri.Port;
            return true;
        }

        port = 0;
        return false;
    }
}
