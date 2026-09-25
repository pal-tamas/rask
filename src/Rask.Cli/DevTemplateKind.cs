using Rask.Cli.Scaffolding;

namespace Rask.Cli;

/// <summary>Which template shape <c>rask dev</c> is looking at, which decides how (and whether) to run it.</summary>
internal enum DevTemplateKind
{
    /// <summary>An ASP.NET host — <c>rask new</c>'s default.</summary>
    Server,

    /// <summary>A wasm-hosted solution; the project to run is the <c>.Server</c> host, not the client.</summary>
    WasmHosted,

    /// <summary>
    ///     A TypeScript front end on an ASP.NET host. Two processes: the host, and the bundler's own dev
    ///     server.
    /// </summary>
    SpaHosted,

    /// <summary>
    ///     A meta framework — Nuxt, Next, SvelteKit and the rest — on an ASP.NET host. Two processes, like
    ///     <see cref="SpaHosted" />, and the same division of labour: the framework's own dev server owns
    ///     the front end for the session and the browser talks to it.
    /// </summary>
    MetaHosted,

    /// <summary>A standalone WebAssembly app: no ASP.NET host, and no launch profile scaffolded.</summary>
    WasmStandalone,

    /// <summary>Something else. Treated like <see cref="Server" />, minus the banner URL.</summary>
    Unknown
}
