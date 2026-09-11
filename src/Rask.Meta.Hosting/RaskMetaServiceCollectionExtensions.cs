using System.Globalization;
using System.Reflection;
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
    ///     Registration is where the options live, rather than at
    ///     <see cref="RaskMetaEndpointExtensions.UseRaskMeta" />, because the supervisor needs them
    ///     before the pipeline is built — it has to start the process and wait for it to listen while
    ///     the app is still coming up.
    /// </remarks>
    /// <param name="services">The app's service collection.</param>
    /// <param name="configure">Adjusts <see cref="MetaHostingOptions" />.</param>
    /// <returns><paramref name="services" />, for chaining.</returns>
    public static IServiceCollection AddRaskMeta(
        this IServiceCollection services,
        Action<MetaHostingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new MetaHostingOptions();

        // What the build baked first, then whatever the app says. That order is the contract: naming
        // the framework in the .csproj is the ordinary way, because the build needs it there anyway to
        // know what to publish — and configure() stays able to override for a framework this package
        // has no preset for, or an app that resolves its front end some other way.
        MetaMetadata.Apply(options, Assembly.GetEntryAssembly());

        configure?.Invoke(options);

        ApplyDevServer(options, Environment.GetEnvironmentVariable);

        ApplyEditorDevServer(
            options,
            EditorDevSession.IsActive(
                Assembly.GetEntryAssembly(),
                IsDevelopment(Environment.GetEnvironmentVariable),
                Environment.GetEnvironmentVariable));

        services.TryAddSingleton(options);
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
    ///         Applied AFTER <c>configure</c>, which is the one place the ordinary precedence is inverted.
    ///         An app that pins <c>o.Port</c> for production would otherwise silently defeat every dev
    ///         session on a framework whose dev server listens somewhere else — and this variable is set
    ///         by the dev tool for the life of one session, not configuration anyone deploys.
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

    /// <summary>
    ///     Runs the framework's own dev server under the supervisor, for an app an editor launched as a dev
    ///     session.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The F5 twin of <see cref="ApplyDevServer" />. A dev-session build skipped the production front-end
    ///         build, so there is no server entry to run — and under a debugger there is no <c>rask dev</c>
    ///         running the dev server beside the app either. So the supervisor runs that dev server itself, on
    ///         the framework's own port, and forwards to it.
    ///     </para>
    ///     <para>
    ///         Skipped when supervision is already off: that is <c>rask dev</c> having pointed the host at a
    ///         dev server it started, or an app that runs its front end some other way on purpose.
    ///     </para>
    /// </remarks>
    internal static void ApplyEditorDevServer(MetaHostingOptions options, bool editorSession)
    {
        if (!editorSession || !options.SuperviseNode)
        {
            return;
        }

        options.RunDevServer = true;
        options.Port = options.Framework.DevServerPort;
    }

    /// <summary>
    ///     Whether the environment names Development — read from the variables, because the host environment
    ///     is not built yet when services are registered. The same precedence the web host itself applies.
    /// </summary>
    internal static bool IsDevelopment(Func<string, string?> readEnv) =>
        string.Equals(
            readEnv("ASPNETCORE_ENVIRONMENT") ?? readEnv("DOTNET_ENVIRONMENT"),
            Environments.Development,
            StringComparison.OrdinalIgnoreCase);

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
