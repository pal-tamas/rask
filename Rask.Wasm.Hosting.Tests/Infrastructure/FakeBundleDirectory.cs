namespace Rask.Wasm.Hosting.Tests.Infrastructure;

internal sealed class FakeBundleDirectory : IDisposable
{
    public FakeBundleDirectory(int wasmPaddingBytes = 0)
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "rask-wasm-hosting-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
        Directory.CreateDirectory(System.IO.Path.Combine(Path, "_framework"));
        Directory.CreateDirectory(System.IO.Path.Combine(Path, "js"));

        File.WriteAllText(System.IO.Path.Combine(Path, "index.html"),
            "<!doctype html><html><body data-rask-root>fake</body></html>");

        // Header: \0asm (the WASM magic). Optional padding lets compression tests get a
        // payload above ResponseCompression's minimum body-size threshold; default keeps
        // the existing 4-byte file for the original MIME/cache tests.
        var wasmBytes = new byte[4 + wasmPaddingBytes];
        wasmBytes[0] = 0x00; wasmBytes[1] = 0x61; wasmBytes[2] = 0x73; wasmBytes[3] = 0x6D;
        // Repeating zeroes compress extremely well — brotli output is tens of bytes vs the
        // raw KB, so the assertions reading Content-Length see a clear win.
        File.WriteAllBytes(System.IO.Path.Combine(Path, "_framework", "foo.wasm"), wasmBytes);
        File.WriteAllText(System.IO.Path.Combine(Path, "js", "app.js"),
            "console.log('hi');");
        File.WriteAllText(System.IO.Path.Combine(Path, "unknown.bin"),
            "opaque");
    }

    public string Path { get; }

    public void Dispose()
    {
        try { Directory.Delete(Path, true); }
        catch
        {
            /* best effort */
        }
    }
}
