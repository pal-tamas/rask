using System.Reflection;

namespace Rask.Hosting.Shared;

/// <summary>
///     Whether this process is a dev session an EDITOR launched — VS Code's F5, which builds with
///     <c>RaskDevSession=true</c> and then runs the app under its debugger — rather than one <c>rask dev</c>
///     is running, or no dev session at all.
/// </summary>
/// <remarks>
///     <para>
///         The difference matters because <c>rask dev</c> starts the front-end dev servers and prepares the
///         <c>https://&lt;name&gt;.test</c> address itself, beside the app. Under F5 nothing does, so the
///         app has to — and must not also do it under <c>rask dev</c>, or two dev servers fight for one port.
///     </para>
///     <para>
///         Three facts, all required. The BUILD said it was a dev session (baked as assembly metadata only
///         when the property was set, so a leftover can never turn an ordinary run into one). The host is in
///         Development. And <c>dotnet watch</c> is not the parent: it sets <c>DOTNET_WATCH=1</c> on the app
///         it runs, and <c>rask dev</c> always runs the app through it.
///     </para>
///     <para>
///         Source-linked (pure BCL) into every assembly that needs it, including the CLI, so the metadata
///         key and the line the scaffolded <c>launch.json</c> waits for have exactly one definition.
///     </para>
/// </remarks>
internal static class EditorDevSession
{
    /// <summary>The assembly-metadata key a dev-session build bakes.</summary>
    internal const string MetadataKey = "Rask.DevSession";

    /// <summary>Set by <c>dotnet watch</c> on the process it runs.</summary>
    internal const string WatchVariable = "DOTNET_WATCH";

    /// <summary>
    ///     The start of the line an editor-launched app logs once it knows where the browser should go. The
    ///     scaffolded <c>launch.json</c>'s <c>serverReadyAction.pattern</c> matches it, and a test holds the
    ///     two together.
    /// </summary>
    internal const string OpenLinePrefix = "Rask dev: open ";

    /// <summary>Whether the build that produced <paramref name="assembly" /> was a dev session.</summary>
    internal static bool IsDevSessionBuild(Assembly? assembly)
    {
        if (assembly is null)
        {
            return false;
        }

        foreach (var attribute in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (string.Equals(attribute.Key, MetadataKey, StringComparison.Ordinal)
                && string.Equals(attribute.Value, "true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Whether the app should run its own dev servers: a dev-session build, in Development, not under
    ///     <c>dotnet watch</c>.
    /// </summary>
    /// <param name="assembly">The app's entry assembly.</param>
    /// <param name="isDevelopment">The host environment's answer.</param>
    /// <param name="readEnv">Reads an environment variable; injected so tests never touch the real one.</param>
    internal static bool IsActive(Assembly? assembly, bool isDevelopment, Func<string, string?> readEnv)
    {
        ArgumentNullException.ThrowIfNull(readEnv);

        return isDevelopment
               && !string.Equals(readEnv(WatchVariable), "1", StringComparison.Ordinal)
               && IsDevSessionBuild(assembly);
    }
}
