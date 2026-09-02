namespace Rask.Meta.Hosting;

/// <summary>
///     Everything this package needs to know about one meta framework: where its built server entry
///     lives, and which environment variables that entry reads to decide what to listen on.
/// </summary>
/// <remarks>
///     <para>
///         Deliberately five fields rather than a class per framework. Six frameworks reduce to three
///         server shapes — Nitro (<see cref="Nuxt" />, <see cref="TanStackStart" />,
///         <see cref="SolidStart" />, <see cref="Analog" />), <c>adapter-node</c>
///         (<see cref="SvelteKit" />) and Next's standalone output (<see cref="Next" />) — and all
///         three produce a single directly executable entry that takes its port from the environment.
///         Once that is true the framework's identity stops mattering to the host, and what is left is
///         data.
///     </para>
///     <para>
///         That single executable entry is also why the supervisor runs <c>node &lt;entry&gt;</c>
///         rather than <c>npm start</c>: npm spawns the real server as a <em>grandchild</em>, so
///         killing npm orphans it. Every one of the six can be executed directly, so the orphan case
///         never has to be handled at all.
///     </para>
/// </remarks>
public sealed record MetaFramework
{
    /// <summary>The framework's name, used in logs and diagnostics.</summary>
    public required string Name { get; init; }

    /// <summary>
    ///     The built server entry, relative to <see cref="MetaHostingOptions.AppDirectory" />.
    /// </summary>
    public required string ServerEntry { get; init; }

    /// <summary>The environment variable the entry reads for its port.</summary>
    public string PortVariable { get; init; } = "PORT";

    /// <summary>
    ///     The environment variable the entry reads for its bind address.
    /// </summary>
    /// <remarks>
    ///     Next's standalone server reads <c>HOSTNAME</c> where everything Nitro-based reads
    ///     <c>HOST</c>. One word, and exactly the kind of difference that silently produces a server
    ///     bound to <c>0.0.0.0</c> when the whole point is that it is reachable only from inside the
    ///     container.
    /// </remarks>
    public string HostVariable { get; init; } = "HOST";

    /// <summary>Nuxt, built with the default <c>node-server</c> Nitro preset.</summary>
    public static MetaFramework Nuxt { get; } = new()
    {
        Name = "nuxt",
        ServerEntry = ".output/server/index.mjs",
    };

    /// <summary>TanStack Start, built with the Vite bundler onto Nitro.</summary>
    /// <remarks>
    ///     The Vite bundler rather than Rsbuild is a pinned choice, not a default. Rsbuild emits a
    ///     fetch-style entry that needs srvx or a custom Node host in front of it, which would add a
    ///     fourth server shape to a seam that otherwise has three.
    /// </remarks>
    public static MetaFramework TanStackStart { get; } = new()
    {
        Name = "tanstack-start",
        ServerEntry = ".output/server/index.mjs",
    };

    /// <summary>SolidStart v2, on Vite with Nitro's <c>node_server</c> preset.</summary>
    public static MetaFramework SolidStart { get; } = new()
    {
        Name = "solidstart",
        ServerEntry = ".output/server/index.mjs",
    };

    /// <summary>AnalogJS — Angular on Vite, with Node as Nitro's default preset.</summary>
    /// <remarks>
    ///     Analog is the one framework here whose output does not sit under <c>.output</c>. It is also
    ///     what lets Angular join this lane on the same terms as everything else: the SPA lane has to
    ///     carve Angular out entirely, because there the build belongs to the Angular CLI rather than
    ///     to Vite.
    /// </remarks>
    public static MetaFramework Analog { get; } = new()
    {
        Name = "analog",
        ServerEntry = "dist/analog/server/index.mjs",
    };

    /// <summary>SvelteKit, built with <c>adapter-node</c>.</summary>
    /// <remarks>
    ///     The one framework whose output is not self-contained: <c>adapter-node</c> emits a server
    ///     that still resolves its production dependencies from <c>node_modules</c>, so the image needs
    ///     an <c>npm ci --omit=dev</c> that the Nitro four do not.
    /// </remarks>
    public static MetaFramework SvelteKit { get; } = new()
    {
        Name = "sveltekit",
        ServerEntry = "build/index.js",
    };

    /// <summary>Next.js, built with <c>output: 'standalone'</c>.</summary>
    /// <remarks>
    ///     <para>
    ///         Standalone deliberately omits <c>public</c> and <c>.next/static</c> — Next's own docs say
    ///         those are "ideally served by a CDN". <b>This package does not serve them yet</b>, and
    ///         nothing else will: the standalone server does not have those directories, so until the
    ///         build targets land, a Next app needs them copied next to <c>server.js</c> (the <c>cp</c>
    ///         Next's own Docker guidance gives) or its assets 404.
    ///     </para>
    ///     <para>
    ///         Serving them from Kestrel is the plan rather than the state: this topology already puts
    ///         Kestrel in front with the cache rules to do it well, which is the one place Next's
    ///         CDN assumption suits this arrangement better than it suits a plain Node deployment.
    ///     </para>
    ///     <para><c>HOSTNAME</c> rather than <c>HOST</c>; see <see cref="HostVariable" />.</para>
    /// </remarks>
    public static MetaFramework Next { get; } = new()
    {
        Name = "nextjs",
        ServerEntry = "server.js",
        HostVariable = "HOSTNAME",
    };
}
