using System.Text;

namespace Rask.DevTools.Endpoints;

/// <summary>
///     The scripts this assembly embeds, bundled from <c>Resources/</c> at build time.
/// </summary>
internal static class DevToolsScripts
{
    /// <summary>The host script's manifest resource name, as <c>Rask.DevTools.csproj</c> embeds it.</summary>
    internal const string HostResourceName = "Rask.DevTools.Resources.rask-devtools-host.js";

    /// <summary>Reads the host script. Called once, when the endpoint is mapped.</summary>
    internal static string LoadHost()
    {
        var asm = typeof(DevToolsScripts).Assembly;
        using var stream = asm.GetManifestResourceStream(HostResourceName)
                           ?? throw new InvalidOperationException(
                               $"The Rask DevTools host script is missing from {asm.GetName().Name} "
                               + $"{asm.GetName().Version}. This is a packaging fault rather than anything in "
                               + "your app: the assembly should embed rask-devtools-host.js. Clear obj/ and bin/ "
                               + "and rebuild; if it persists, reinstall the package, and please report it with "
                               + "the assembly version above.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
