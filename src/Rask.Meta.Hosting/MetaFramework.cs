namespace Rask.Meta.Hosting;

/// <summary>
///     Everything this package needs to know about one meta framework: where its built server entry
///     lives, and which environment variables that entry reads to decide what to listen on.
/// </summary>
/// <remarks>
///     <para>
///         Deliberately a handful of fields rather than a class per framework. Six frameworks reduce to three
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

    /// <summary>
    ///     The directory the server entry is run from, relative to
    ///     <see cref="MetaHostingOptions.AppDirectory" />. Empty means the app directory itself.
    /// </summary>
    /// <remarks>
    ///     Each framework documents its own invocation and they do not agree. Nitro's and SvelteKit's
    ///     are run from the app root — <c>node .output/server/index.mjs</c> — while Next's standalone
    ///     output is documented as being copied to the image's working directory and started with
    ///     <c>node server.js</c> from inside it. Taking the framework's own word for this is safer than
    ///     assuming any of them resolves paths from <c>__dirname</c>.
    /// </remarks>
    public string WorkingSubdirectory { get; init; } = string.Empty;

    /// <summary>
    ///     The framework's built client assets, served by Kestrel rather than forwarded.
    /// </summary>
    /// <remarks>
    ///     Nitro's four converge again here, on <c>.output/public</c> — the same convergence that makes
    ///     <see cref="ServerEntry" /> data rather than code. Next is the only one needing two, because
    ///     its standalone output omits both and they live in different places.
    /// </remarks>
    public IReadOnlyList<StaticRoot> StaticRoots { get; init; } = [];

    /// <summary>The client assets of a Nitro build, wherever its output root is.</summary>
    private static IReadOnlyList<StaticRoot> NitroPublic(string outputRoot) =>
        [new StaticRoot(string.Empty, outputRoot + "/public")];

    /// <summary>
    ///     The preset with this <see cref="Name" />, or null when nothing matches.
    /// </summary>
    /// <remarks>
    ///     The other half of the build's framework table: the name written in the <c>.csproj</c> is
    ///     baked into the assembly, and this is what turns it back into a preset at startup. Kept
    ///     internal because the string form is the build's business — an app naming a framework in C#
    ///     has the presets themselves to hand.
    /// </remarks>
    internal static MetaFramework? ByName(string name) => name switch
    {
        "nuxt" => Nuxt,
        "tanstack-start" => TanStackStart,
        "solidstart" => SolidStart,
        "analog" => Analog,
        "sveltekit" => SvelteKit,
        "nextjs" => Next,
        _ => null,
    };

    /// <summary>Nuxt, built with the default <c>node-server</c> Nitro preset.</summary>
    public static MetaFramework Nuxt { get; } = new()
    {
        Name = "nuxt",
        ServerEntry = ".output/server/index.mjs",
        StaticRoots = NitroPublic(".output"),
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
        StaticRoots = NitroPublic(".output"),
    };

    /// <summary>SolidStart v2, on Vite with Nitro's <c>node_server</c> preset.</summary>
    public static MetaFramework SolidStart { get; } = new()
    {
        Name = "solidstart",
        ServerEntry = ".output/server/index.mjs",
        StaticRoots = NitroPublic(".output"),
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
        StaticRoots = NitroPublic("dist/analog"),
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
        StaticRoots = [new StaticRoot(string.Empty, "build/client")],
    };

    /// <summary>Next.js, built with <c>output: 'standalone'</c>.</summary>
    /// <remarks>
    ///     <para>
    ///         Standalone deliberately omits <c>public</c> and <c>.next/static</c> — Next's own docs say
    ///         those are "ideally served by a CDN", so its server does not carry them. Under this
    ///         topology Kestrel already <em>is</em> the thing in front, and it serves both from
    ///         <see cref="StaticRoots" />. What reads as Next's awkward case elsewhere, needing a
    ///         hand-written <c>cp</c> in the Dockerfile, is the one place this arrangement suits it
    ///         better than the CDN it assumes.
    ///     </para>
    ///     <para>
    ///         The two roots differ in kind, which is why there are two: <c>public</c> is a source
    ///         directory served at the site root, and <c>.next/static</c> is build output served under
    ///         the <c>/_next/static</c> prefix Next's own markup points at.
    ///     </para>
    ///     <para><c>HOSTNAME</c> rather than <c>HOST</c>; see <see cref="HostVariable" />.</para>
    /// </remarks>
    public static MetaFramework Next { get; } = new()
    {
        Name = "nextjs",
        ServerEntry = ".next/standalone/server.js",
        WorkingSubdirectory = ".next/standalone",
        HostVariable = "HOSTNAME",
        StaticRoots =
        [
            new StaticRoot(string.Empty, "public"),
            new StaticRoot("/_next/static", ".next/static"),
        ],
    };
}
