using Microsoft.Extensions.DependencyInjection;

namespace Rask.Core.Diagnostics.DevTools;

/// <summary>
///     What <c>Rask.DevTools</c> implements so a host can attach it without referencing it.
/// </summary>
internal interface IRaskDevToolsBootstrap
{
    /// <summary>Registers the devtools' services. Called once, before the host builds its provider.</summary>
    void Attach(IServiceCollection services);
}

/// <summary>
///     Attaches <c>Rask.DevTools</c> to a host when the build carries it — with no code in the app.
/// </summary>
/// <remarks>
///     <para>
///         The hosts cannot reference the devtools package (it references them), and an app is not meant
///         to write anything to get it: a Debug build of a scaffolded app simply has it. So the host asks
///         for the bootstrap BY NAME. An app without the package, or a Release build that stripped it,
///         finds no such type and the call is a no-op.
///     </para>
///     <para>
///         The name is a constant on purpose. The trimmer resolves a constant <see cref="Type.GetType(string, bool)" />
///         argument itself, so in a Debug trimmed publish — where <c>Rask.DevTools.targets</c> also roots the
///         assembly — the type and its constructor are kept, and in a build without the assembly there is
///         nothing to resolve and nothing to warn about. A <c>DynamicDependency</c> here would instead warn
///         (IL2035) in every trimmed app that never referenced the package.
///     </para>
///     <para>
///         Not a module initializer in the devtools assembly: nothing in the app references that assembly,
///         so nothing would ever load it to run one.
///     </para>
/// </remarks>
internal static class RaskDevToolsLoader
{
    /// <summary>The bootstrap's assembly-qualified name.</summary>
    internal const string BootstrapTypeName = "Rask.DevTools.DevToolsBootstrap, Rask.DevTools";

    /// <summary>
    ///     Attaches the devtools when the build carries them. Returns whether a bootstrap was found and ran.
    /// </summary>
    internal static bool TryAttach(IServiceCollection services) =>
        RaskDevToolsFeature.IsEnabled && Attach(services);

    /// <summary>The switch-independent half, so a test can drive both outcomes in one process.</summary>
    internal static bool Attach(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var type = Type.GetType(BootstrapTypeName, throwOnError: false);
        if (type is null || Activator.CreateInstance(type) is not IRaskDevToolsBootstrap bootstrap)
        {
            return false;
        }

        bootstrap.Attach(services);
        return true;
    }
}
