namespace Rask.Cli.E2E.Tests;

/// <summary>The two templates whose islands <c>rask dev</c> serves from a Vite dev server.</summary>
/// <remarks>
///     Not <c>wasm-hosted</c>: its islands are the browser half's, bundled by the build of the project it
///     generates into <c>obj/</c>, and <c>rask dev</c> starts no island dev server for it (DevTarget.HasIslands).
/// </remarks>
internal enum IslandHost
{
    /// <summary>The <c>server</c> template: the page is stamped <c>data-rask-islands-dev</c> as well.</summary>
    Server,

    /// <summary>The <c>wasm</c> template: a static page, which learns of the dev server from the manifest alone.</summary>
    Wasm,
}
