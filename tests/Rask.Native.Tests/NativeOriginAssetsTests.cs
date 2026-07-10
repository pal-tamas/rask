using System.Text;
using Rask.Native;
using Xunit;

namespace Rask.Native.Tests;

// Pure unit coverage for the native-origin asset table. The same Resolve() drives the on-device WebView
// interceptors (samples/Rask.Example.Native) and the NativeAssetHttpHandler, so pinning the routing +
// content types here guards both.
public sealed class NativeOriginAssetsTests
{
    // A fake static-file store: keys are origin-relative paths (leading '/' stripped).
    private static readonly Dictionary<string, byte[]> Files = new(StringComparer.Ordinal)
    {
        ["global.css"] = "body{}"u8.ToArray(),
        ["data/posts-1.json"] = "[]"u8.ToArray(),
        ["img/rask-mark.svg"] = "<svg/>"u8.ToArray(),
        ["_content/Rask.Bootstrap/dist/css/bootstrap.min.css"] = ".btn{}"u8.ToArray()
    };

    private static byte[]? Read(string rel) => Files.GetValueOrDefault(rel);

    [Theory]
    [InlineData("/")]
    [InlineData("/index.html")]
    [InlineData("/index.native.html")]   // the device heads (WKWebView / Android WebView) load this path
    public void Root_serves_the_boot_shell(string path)
    {
        var asset = NativeOriginAssets.Resolve(path, Read);

        Assert.NotNull(asset);
        Assert.Equal("text/html", asset!.Value.ContentType);
        Assert.Equal(NativeClientAssets.IndexHtml, Encoding.UTF8.GetString(asset.Value.Body));
    }

    [Fact]
    public void Client_path_serves_the_native_client_js()
    {
        var asset = NativeOriginAssets.Resolve("/rask.native.js", Read);

        Assert.NotNull(asset);
        Assert.Equal("text/javascript", asset!.Value.ContentType);
        Assert.Equal(NativeClientAssets.ClientJs, Encoding.UTF8.GetString(asset.Value.Body));
    }

    [Theory]
    [InlineData("global.css", "/global.css", "text/css")]
    [InlineData("data/posts-1.json", "/data/posts-1.json", "application/json")]
    [InlineData("img/rask-mark.svg", "/img/rask-mark.svg", "image/svg+xml")]
    [InlineData("_content/Rask.Bootstrap/dist/css/bootstrap.min.css",
        "/_content/Rask.Bootstrap/dist/css/bootstrap.min.css", "text/css")]
    public void Static_files_are_served_from_the_reader_with_the_right_content_type(
        string key, string path, string expectedContentType)
    {
        var asset = NativeOriginAssets.Resolve(path, Read);

        Assert.NotNull(asset);
        Assert.Equal(expectedContentType, asset!.Value.ContentType);
        Assert.Equal(Files[key], asset.Value.Body);
    }

    [Fact]
    public void Unknown_scoped_asset_hash_falls_through_to_null() =>
        // No such scoped asset is registered → the /_rask/a/ branch declines and the reader has no match.
        Assert.Null(NativeOriginAssets.Resolve("/_rask/a/deadbeef.css", Read));

    [Fact]
    public void Unknown_path_returns_null_so_the_caller_picks_its_fallback() =>
        Assert.Null(NativeOriginAssets.Resolve("/does/not/exist.png", Read));

    [Fact]
    public void Boot_shell_does_not_depend_on_the_static_file_reader() =>
        // The shell/client come from NativeClientAssets, never the reader — a throwing reader must not matter.
        Assert.NotNull(NativeOriginAssets.Resolve("/", _ => throw new InvalidOperationException()));

    [Theory]
    [InlineData("x.woff2", "font/woff2")]
    [InlineData("x.json", "application/json")]
    [InlineData("x.unknownext", "application/octet-stream")]
    public void ContentTypeFor_maps_by_extension(string path, string expected) =>
        Assert.Equal(expected, NativeOriginAssets.ContentTypeFor(path));
}
