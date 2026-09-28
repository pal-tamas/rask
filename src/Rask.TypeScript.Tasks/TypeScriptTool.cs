namespace Rask.TypeScript.Tasks;

/// <summary>The tools Rask fetches to work with TypeScript without npm.</summary>
/// <remarks>
///     esbuild and tsgo are deliberately separate tools rather than one. esbuild strips types and bundles but
///     does not type-check; tsgo type-checks but does not bundle. Using esbuild alone would mean TypeScript
///     with none of the guarantee that makes it worth writing, and using tsgo alone would mean shipping
///     unbundled, unminified modules. The compiler's JavaScript API is a third thing again: the one stable way
///     to ask the checker what a type IS, which is how a package island reads a component's props.
/// </remarks>
public enum TypeScriptTool
{
    /// <summary>The bundler: type-strip, resolve imports, downlevel, minify.</summary>
    Esbuild,

    /// <summary>The type checker — the native Go build of the TypeScript compiler.</summary>
    Tsgo,

    /// <summary>
    ///     The TypeScript compiler as a JavaScript library, for its programmatic checker API.
    /// </summary>
    /// <remarks>
    ///     Not a binary: it runs under Node, and only where a project already has Node because it builds
    ///     islands. tsgo has no stable programmatic API yet, and a props snapshot needs the checker's own
    ///     answer about a type rather than a reading of the declaration text.
    /// </remarks>
    TypeScript,
}
