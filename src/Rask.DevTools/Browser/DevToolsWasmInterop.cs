using System.Runtime.InteropServices.JavaScript;
using System.Text;

namespace Rask.DevTools.Browser;

// `partial` because the JSImport generator writes the method bodies into a second declaration.
internal static partial class DevToolsWasmInterop
{
    private const string ModuleName = "rask-devtools";

    /// <summary>
    ///     Imports the devtools module from its source. A data URL rather than a static web asset: the source is embedded in
    ///     this assembly, so a Release publish that strips the assembly strips the script with it, and no asset path has to
    ///     be mapped or guarded. A page whose Content-Security-Policy forbids data: scripts blocks it, which the caller
    ///     reports.
    /// </summary>
    internal static Task ImportAsync(string moduleSource) =>
        JSHost.ImportAsync(
            ModuleName,
            "data:text/javascript;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(moduleSource)));

    [JSImport("install", ModuleName)]
    internal static partial void Install(
        string frameDocument,
        [JSMarshalAs<JSType.Function>] Action onOpen,
        [JSMarshalAs<JSType.Function<JSType.String>>] Action<string> onEvent);

    [JSImport("deliver", ModuleName)]
    internal static partial void Deliver([JSMarshalAs<JSType.MemoryView>] Span<byte> frame);
}
