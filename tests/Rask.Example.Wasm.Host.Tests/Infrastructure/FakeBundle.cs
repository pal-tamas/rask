namespace Rask.Example.Wasm.Host.Tests.Infrastructure;

// Minimal AppBundle directory layout for testing the static-file host pipeline
// without forcing a full Rask.Example.Wasm publish during the test run.
internal sealed class FakeBundle : IDisposable
{
    public FakeBundle()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "rask-example-wasm-host-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
        Directory.CreateDirectory(System.IO.Path.Combine(Path, "_framework"));

        File.WriteAllText(System.IO.Path.Combine(Path, "index.html"),
            "<!doctype html><html><body data-rask-root>fake bundle</body></html>");

        // Minimal valid-shaped WASM payload (magic header) so the MIME-mapping path
        // serves it as application/wasm.
        File.WriteAllBytes(System.IO.Path.Combine(Path, "_framework", "dotnet.wasm"),
            new byte[] { 0x00, 0x61, 0x73, 0x6D });
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
