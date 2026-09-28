namespace Rask.Core.Live;

/// <summary>
///     Static accessor for the process-wide live options that back the content-addressed asset
///     registries. Set by <c>AddRask()</c> / <c>MapRask&lt;TApp&gt;()</c> from the configured
///     <see cref="RaskLiveOptions" />. <see cref="PathBase" /> and <see cref="MinifyScopedAssets" />
///     live here because the <see cref="ScopedAssets.ScopedAssetRegistry" /> and
///     <c>HeadAssetRegistry</c> build one shared, content-hashed bundle per process — not per session.
///     The per-render <c>DiffMode</c>, by contrast, is carried on each <c>LiveSession</c>
///     (see <see cref="LiveSessionBase" />) so concurrent hosts and parallel tests don't race a
///     shared mutable field. Hosts that don't go through <c>AddRask()</c> (some standalone WASM
///     bootstraps) can also write these properties directly.
/// </summary>
public static class LiveOptions
{
    private static string _pathBase = string.Empty;

    /// <summary>
    ///     Resolved scoped-CSS minification switch read by <see cref="ScopedAssets.ScopedAssetRegistry" />
    ///     when it builds the bundle. <c>null</c> (default) = unresolved/off — <c>MapRask</c> resolves the
    ///     auto default from <see cref="RaskLiveOptions.MinifyScopedAssets" /> + the host environment; a
    ///     standalone host (or a test) can also set it directly.
    /// </summary>
    public static bool? MinifyScopedAssets { get; set; }

    /// <summary>
    ///     Whether the app is running in Development, as decided by the <em>host</em>. <c>null</c>
    ///     (default) = unresolved, in which case <see cref="Components.DefaultErrorPage" /> falls back to
    ///     reading the standard ASP.NET environment variables itself.
    /// </summary>
    /// <remarks>
    ///     This decides whether an error page shows a stack trace and a source excerpt or just a type and
    ///     a message, so getting it wrong is expensive in exactly the moment it matters. Core cannot ask
    ///     the host directly — it takes no dependency on <c>Microsoft.Extensions.Hosting</c>, deliberately
    ///     — and the environment-variable fallback it used instead is only correct when the environment
    ///     arrived that way. <c>dotnet run --environment Development</c>, <c>appsettings.json</c>,
    ///     assigning <c>builder.Environment.EnvironmentName</c>, and IDE profiles that set configuration
    ///     rather than the process environment all select Development without setting a variable, and all
    ///     of them silently produced the production error page while developing (#605). <c>MapRask</c>
    ///     now resolves this from <c>IWebHostEnvironment</c>; a standalone host or a test can set it
    ///     directly. Host-wide rather than per-session, like <see cref="MinifyScopedAssets" />.
    /// </remarks>
    public static bool? IsDevelopment { get; set; }

    /// <summary>
    ///     The <see cref="AppContext" /> data name <see cref="PathBase" /> is also published under, for a package that
    ///     must know the deploy's path base without referencing <c>Rask.Core</c>.
    /// </summary>
    /// <remarks>
    ///     Rask.Storage is the reader: Core travels only inside the hosts that render components, so a battery that
    ///     named this class could not start on the SPA or meta lanes (#1086). Keep the string in step with its copy
    ///     there, which a Storage test pins.
    /// </remarks>
    internal const string PathBaseDataName = "Rask.PathBase";

    /// <summary>
    ///     Active URL prefix (see <see cref="RaskLiveOptions.PathBase" />).
    ///     Always normalized: <c>""</c> or <c>"/segment"</c> (no trailing slash).
    /// </summary>
    public static string PathBase
    {
        get => _pathBase;
        set
        {
            _pathBase = RaskPath.Normalize(value);
            AppContext.SetData(PathBaseDataName, _pathBase);
        }
    }
}
