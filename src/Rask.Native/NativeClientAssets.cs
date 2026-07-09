using System.Reflection;
using System.Resources;

namespace Rask.Native;

/// <summary>
///     The embedded native client assets a platform head serves into its WebView: the boot shell
///     (<c>index.native.html</c>) and the spliced client runtime (<c>rask.native.js</c>). The iOS
///     head serves these through a <c>WKURLSchemeHandler</c> (a real <c>app://</c> origin, so
///     <c>localStorage</c>/<c>crypto.subtle</c>/secure-context device APIs work); the Android head
///     serves them through a <c>WebViewAssetLoader</c>. Both are read once and cached.
/// </summary>
public static class NativeClientAssets
{
    private static string? _indexHtml;
    private static string? _clientJs;

    /// <summary>The boot shell HTML. Minimal — no <c>dotnet.js</c>; the .NET runtime is the host process.</summary>
    public static string IndexHtml => _indexHtml ??= Read("Rask.Native.index.native.html");

    /// <summary>
    ///     The native client runtime (<c>rask.native.js</c>): the shared diff/morph/interop modules plus
    ///     the native transport shim (<c>send()</c> → the platform bridge, <c>applyRender()</c> invoked
    ///     by native). Served at the path the boot shell's <c>&lt;script&gt;</c> references.
    /// </summary>
    public static string ClientJs => _clientJs ??= Read("Rask.Native.rask.native.js");

    private static string Read(string logicalName)
    {
        var asm = typeof(NativeClientAssets).Assembly;
        using var stream = asm.GetManifestResourceStream(logicalName)
                           ?? throw new MissingManifestResourceException(
                               $"Embedded native client asset '{logicalName}' not found in {asm.GetName().Name}. " +
                               "Ensure the _RaskSpliceNativeClientJs build target ran (it generates Assets/rask.native.js).");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
