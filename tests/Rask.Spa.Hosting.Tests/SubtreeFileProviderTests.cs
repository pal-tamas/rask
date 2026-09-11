using Microsoft.Extensions.FileProviders;

namespace Rask.Spa.Hosting.Tests;

/// <summary>
///     The one subtree of a WebAssembly bundle that <c>UseRaskSpa</c> adds to the host's web root.
/// </summary>
/// <remarks>
///     Only <c>_rask/a/</c> may be reachable through it. Anything else would let a plain
///     <c>UseStaticFiles()</c> serve the bundle again at the site root, outside the prefix it was mounted under.
/// </remarks>
public sealed class SubtreeFileProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rask-subtree-" + Guid.NewGuid().ToString("N"));
    private readonly SubtreeFileProvider _provider;

    public SubtreeFileProviderTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "_rask", "a"));
        File.WriteAllText(Path.Combine(_root, "_rask", "a", "0123456789ab.css"), ".x{}");
        File.WriteAllText(Path.Combine(_root, "index.html"), "<!doctype html>");
        _provider = new SubtreeFileProvider(new PhysicalFileProvider(_root), "_rask/a/");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temp directory left behind is not worth failing a run over.
        }
    }

    [Theory]
    [InlineData("_rask/a/0123456789ab.css")]
    [InlineData("/_rask/a/0123456789ab.css")]
    public void A_file_inside_the_subtree_is_found(string subpath) =>
        Assert.True(_provider.GetFileInfo(subpath).Exists);

    [Theory]
    [InlineData("index.html")]
    [InlineData("/index.html")]
    // The prefix matches; the file is still outside it.
    [InlineData("_rask/a/../../index.html")]
    [InlineData("_rask/a/..\\..\\index.html")]
    [InlineData("/_rask/a/../../index.html")]
    public void Nothing_outside_the_subtree_is_found(string subpath) =>
        Assert.False(_provider.GetFileInfo(subpath).Exists);

    [Fact]
    public void A_directory_outside_the_subtree_is_not_listed() =>
        Assert.False(_provider.GetDirectoryContents("_rask/a/../..").Exists);
}
