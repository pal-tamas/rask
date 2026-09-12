using System.Reflection;

namespace Rask.DevTools.Browser;

/// <summary>The devtools' browser scripts, embedded in this assembly by the build.</summary>
internal static class DevToolsBrowserScripts
{
    private const string ModuleResource = "Rask.DevTools.Resources.rask-devtools-wasm.js";
    private const string FrameResource = "Rask.DevTools.Resources.rask-devtools-frame.js";

    /// <summary>The module the browser face imports: the pill, the drawer and the bridge to the panel frame.</summary>
    internal static string LoadModule() => Load(ModuleResource);

    /// <summary>
    ///     The panel frame's document: an empty page that runs the frame client. The panel's first frame morphs the rest in;
    ///     the script is marked managed so that morph keeps it.
    /// </summary>
    internal static string FrameDocument() =>
        "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><script data-rask-managed>"
        // A script's text ends at the first "</script", whatever surrounds it.
        + Load(FrameResource).Replace("</script", "<\\/script", StringComparison.OrdinalIgnoreCase)
        + "</script></head><body></body></html>";

    private static string Load(string name)
    {
        using var stream = typeof(DevToolsBrowserScripts).Assembly.GetManifestResourceStream(name)
                           ?? throw new InvalidOperationException(
                               $"Rask.DevTools is missing its embedded '{name}'. This is a packaging fault in Rask.DevTools.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
