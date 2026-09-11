namespace Rask.Spa.Hosting.Tests;

/// <summary>
///     The one judgement in this package that cannot be taken back: a wrong <c>immutable</c> sits in
///     every visitor's disk cache for a year and is only curable by renaming the file. So the negatives
///     matter more than the positives here.
/// </summary>
public class SpaCacheClassificationTests
{
    [Theory]
    // Vite: hashed, under the assets prefix.
    [InlineData("/assets/index-DkK9xYz1.js", "index-DkK9xYz1.js")]
    [InlineData("/assets/index-a1b2c3d4.css", "index-a1b2c3d4.css")]
    // Anything under the hashed prefix, by the bundler's own guarantee.
    [InlineData("/assets/logo.svg", "logo.svg")]
    // Angular hashes at the dist root instead, which is what the filename rule is for.
    [InlineData("/main-ABCD1234.js", "main-ABCD1234.js")]
    // Create React App's older dot-separated shape.
    [InlineData("/static/js/main.9a3f1c2b.js", "main.9a3f1c2b.js")]
    public void A_content_hashed_asset_may_be_cached_for_ever(string path, string file) =>
        Assert.True(SpaCacheClassification.IsImmutable(path, file, new SpaHostingOptions()));

    [Theory]
    // The entry document, always and first — freezing it strands a visitor on the deploy they
    // first saw, including the script tags naming every other file.
    [InlineData("/index.html", "index.html")]
    [InlineData("/assets/index.html", "index.html")]
    // A long name is not a hash. This is the case the digit rule exists for: it clears the length
    // bar and would otherwise be frozen for a year.
    [InlineData("/vendor-somelongname.js", "vendor-somelongname.js")]
    // Short, unhashed, at the root.
    [InlineData("/favicon.svg", "favicon.svg")]
    [InlineData("/manifest.webmanifest", "manifest.webmanifest")]
    [InlineData("/robots.txt", "robots.txt")]
    // A dash with too few characters after it.
    [InlineData("/vendor-react.js", "vendor-react.js")]
    public void Anything_else_revalidates(string path, string file) =>
        Assert.False(SpaCacheClassification.IsImmutable(path, file, new SpaHostingOptions()));

    [Fact]
    public void The_entry_document_beats_the_immutable_prefix()
    {
        // Ordering matters: the prefix rule would otherwise freeze the one file that must never be.
        var options = new SpaHostingOptions();
        options.ImmutablePathPrefixes.Add("/");

        Assert.False(SpaCacheClassification.IsImmutable("/index.html", "index.html", options));
    }

    [Fact]
    public void A_configured_prefix_is_honoured()
    {
        var options = new SpaHostingOptions();
        options.ImmutablePathPrefixes.Clear();
        options.ImmutablePathPrefixes.Add("/static/");

        Assert.True(SpaCacheClassification.IsImmutable("/static/anything.js", "anything.js", options));
        Assert.False(SpaCacheClassification.IsImmutable("/assets/anything.js", "anything.js", options));
    }

    [Theory]
    // The SDK's fingerprint, and a scoped asset named by its content hash.
    [InlineData("/_framework/dotnet.native.7a8b9c2d3e.wasm", "dotnet.native.7a8b9c2d3e.wasm", true)]
    [InlineData("/_rask/a/0123456789ab.css", "0123456789ab.css", true)]
    // The SDK's default, unfingerprinted names — including ones the bundler heuristic would freeze.
    [InlineData("/_framework/dotnet.js", "dotnet.js", false)]
    [InlineData("/_framework/System.IO.Pipelines.wasm", "System.IO.Pipelines.wasm", false)]
    [InlineData("/_framework/Rask.Components-12345678.wasm", "Rask.Components-12345678.wasm", false)]
    // Hand-written files in wwwroot, whatever they are called.
    [InlineData("/assets/logo.svg", "logo.svg", false)]
    [InlineData("/icon-512x512.png", "icon-512x512.png", false)]
    [InlineData("/index.html", "index.html", false)]
    public void A_wasm_bundle_trusts_only_what_its_publish_hashed(string path, string file, bool immutable) =>
        Assert.Equal(immutable, SpaCacheClassification.IsImmutable(path, file, new SpaHostingOptions(), wasm: true));

    [Fact]
    public void A_prefix_added_for_a_wasm_bundle_still_applies()
    {
        var options = new SpaHostingOptions();
        options.ImmutablePathPrefixes.Add("/hashed/");

        Assert.True(SpaCacheClassification.IsImmutable("/hashed/app.css", "app.css", options, wasm: true));
    }

    [Theory]
    [InlineData("/_framework/missing.wasm", true)]
    [InlineData("/_rask/a/missing.css", true)]
    [InlineData("/assets/missing.png", false)]
    [InlineData("/orders/42", false)]
    public void A_wasm_bundle_never_falls_back_under_its_runtime_paths(string path, bool asset) =>
        Assert.Equal(asset, SpaCacheClassification.IsAssetPath(path, new SpaHostingOptions(), wasm: true));
}

public class SpaPathTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("/", "")]
    [InlineData("app", "/app")]
    [InlineData("/app", "/app")]
    [InlineData("/app/", "/app")]
    [InlineData("/app///", "/app")]
    public void Normalize_matches_the_core_implementation(string? input, string expected) =>
        Assert.Equal(expected, SpaPath.Normalize(input));
}
