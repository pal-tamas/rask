namespace Rask.Wasm.Hosting.Tests.Infrastructure;

internal sealed class FakeBundleDirectory : IDisposable
{
    public FakeBundleDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "rask-wasm-hosting-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
        Directory.CreateDirectory(System.IO.Path.Combine(Path, "_framework"));
        Directory.CreateDirectory(System.IO.Path.Combine(Path, "js"));

        File.WriteAllText(System.IO.Path.Combine(Path, "index.html"),
            "<!doctype html><html><body data-rask-root>fake</body></html>");
        File.WriteAllBytes(System.IO.Path.Combine(Path, "_framework", "foo.wasm"),
            new byte[] { 0x00, 0x61, 0x73, 0x6D });
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
