namespace Rask.Spa.Hosting.Tests.Infrastructure;

/// <summary>
///     A throwaway directory shaped like a Vite build: a hashed <c>assets/</c> tree, an entry
///     document, and an unhashed file at the root.
/// </summary>
internal sealed class FakeDistDirectory : IDisposable
{
    public FakeDistDirectory(bool withIndex = true, bool withPrecompressed = false)
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "rask-spa-tests-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(System.IO.Path.Combine(Path, "assets"));

        if (withIndex)
        {
            Write("index.html", "<!doctype html><title>app</title><div id=root></div>");
        }

        // The names matter: this is exactly what Vite emits, and the cache rules are written
        // against it rather than against something tidier.
        Write(System.IO.Path.Combine("assets", "index-DkK9xYz1.js"), "export default 1;");
        Write(System.IO.Path.Combine("assets", "index-a1b2c3d4.css"), ".x{color:red}");
        Write("favicon.svg", "<svg/>");
        Write("manifest.webmanifest", "{}");

        if (withPrecompressed)
        {
            // Not real brotli — nothing decompresses it here. It exists so the middleware has a
            // sibling to find, which is what the content-type regression is about.
            Write(System.IO.Path.Combine("assets", "index-DkK9xYz1.js.br"), "brotli-bytes");
        }
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

    private void Write(string relative, string content) =>
        File.WriteAllText(System.IO.Path.Combine(Path, relative), content);
}
