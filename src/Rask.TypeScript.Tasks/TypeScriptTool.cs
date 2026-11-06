namespace Rask.TypeScript.Tasks;

/// <summary>The tools Rask fetches to work with TypeScript without npm.</summary>
/// <remarks>
///     esbuild and tsgo are deliberately separate tools rather than one. esbuild strips types and bundles but
///     does not type-check; tsgo type-checks but does not bundle. Using esbuild alone would mean TypeScript
///     with none of the guarantee that makes it worth writing, and using tsgo alone would mean shipping
///     unbundled, unminified modules.
/// </remarks>
public enum TypeScriptTool
{
    /// <summary>The bundler: type-strip, resolve imports, downlevel, minify.</summary>
    Esbuild,

    /// <summary>The type checker — the native Go build of the TypeScript compiler.</summary>
    Tsgo,
}
