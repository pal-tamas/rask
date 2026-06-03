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
        wasmBytes[0] = 0x00;
        wasmBytes[1] = 0x61;
        wasmBytes[2] = 0x73;
        wasmBytes[3] = 0x6D;
        // Repeating zeroes compress extremely well — brotli output is tens of bytes vs the
        // raw KB, so the assertions reading Content-Length see a clear win.
        File.WriteAllBytes(System.IO.Path.Combine(Path, "_framework", "foo.wasm"), wasmBytes);
        // Fingerprinted asset name — what the WASM SDK emits when
        // <WasmFingerprintAssets>true</WasmFingerprintAssets> is enabled.
        File.WriteAllBytes(System.IO.Path.Combine(Path, "_framework", "dotnet.7a8b9c2d3e4f.wasm"), wasmBytes);
        // Precompressed siblings for the precompressed-middleware tests. Bytes are
        // distinct enough that the test can prove the sibling was served (not the raw).
        File.WriteAllBytes(System.IO.Path.Combine(Path, "_framework", "compressed.wasm"), wasmBytes);
        File.WriteAllBytes(System.IO.Path.Combine(Path, "_framework", "compressed.wasm.br"),
            new byte[] { 0x42, 0x52, 0x01, 0x02 });
        File.WriteAllBytes(System.IO.Path.Combine(Path, "_framework", "compressed.wasm.gz"),
            new byte[] { 0x1F, 0x8B, 0x08, 0x00, 0x00 });
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
