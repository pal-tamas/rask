namespace Rask.Spa.Hosting.Tests.Infrastructure;

/// <summary>
///     A throwaway directory shaped like a published Rask WebAssembly app: Rask's boot module, the .NET
///     runtime under <c>_framework/</c>, baked scoped assets under <c>_rask/a/</c>, and a hand-written
///     <c>assets/</c> folder of the kind an app keeps in its <c>wwwroot</c>.
/// </summary>
internal sealed class FakeWasmBundleDirectory : IDisposable
{
    /// <summary>A baked scoped stylesheet's name: its content hash.</summary>
    public const string ScopedCss = "0123456789ab.css";

    /// <summary>The bytes of the precompressed <c>compressed.wasm.br</c> sibling.</summary>
    public static readonly byte[] BrotliSibling = [0x42, 0x52, 0x01, 0x02];

    public FakeWasmBundleDirectory(int wasmPaddingBytes = 0)
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "rask-spa-wasm-tests-" + Guid.NewGuid().ToString("N"));

        Write("index.html", "<!doctype html><html><body data-rask-root>fake</body></html>");
        Write("main.js", "import './rask.wasm.js';");
        Write("rask.wasm.js", "export function boot() {}");
        Write(System.IO.Path.Combine("_framework", "dotnet.js"), "export default {};");
        Write(System.IO.Path.Combine("_rask", "a", ScopedCss), ".x{color:red}");
        Write(System.IO.Path.Combine("assets", "logo.svg"), "<svg/>");

        // The WebAssembly magic, optionally padded past ResponseCompression's minimum body size.
        var wasm = new byte[4 + wasmPaddingBytes];
        wasm[1] = 0x61;
        wasm[2] = 0x73;
        wasm[3] = 0x6D;
        WriteBytes(System.IO.Path.Combine("_framework", "foo.wasm"), wasm);

        // The name the SDK writes when WasmFingerprintAssets is on.
        WriteBytes(System.IO.Path.Combine("_framework", "dotnet.native.7a8b9c2d3e4f.wasm"), wasm);

        // ICU data, which no stock content type covers.
        WriteBytes(System.IO.Path.Combine("_framework", "icudt.dat"), [1, 2, 3]);

        WriteBytes(System.IO.Path.Combine("_framework", "compressed.wasm"), wasm);
        WriteBytes(System.IO.Path.Combine("_framework", "compressed.wasm.br"), BrotliSibling);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A temp directory that outlives one test run is not worth failing it over.
        }
    }

    private void Write(string relative, string content)
    {
        var full = System.IO.Path.Combine(Path, relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private void WriteBytes(string relative, byte[] content)
    {
        var full = System.IO.Path.Combine(Path, relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, content);
    }
}
