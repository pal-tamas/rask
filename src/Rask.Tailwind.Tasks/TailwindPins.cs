using System;
using System.Collections.Generic;

namespace Rask.Tailwind.Tasks;

/// <summary>
///     The SHA-256 of every standalone Tailwind CLI asset, for the version Rask pins.
/// </summary>
/// <remarks>
///     <para>
///         The release's own <c>sha256sums.txt</c> comes from the same place as the binary, so checking
///         against it catches a corrupted download and nothing else: whoever can replace the asset can
///         replace the manifest beside it. These are recorded HERE, in the repository, when the pin
///         moves — so the binary a build is about to execute has to be the one that was there on the
///         day somebody looked.
///     </para>
///     <para>
///         <c>scripts/upstream/tools.py</c> moves <c>RaskTailwindVersion</c> and this table together, from
///         assets it downloads and hashes itself, and only when they agree with the release's
///         <c>sha256sums.txt</c>. A test fails while the pinned version has an asset with no entry here.
///     </para>
/// </remarks>
internal static class TailwindPins
{
    /// <summary>The version the table below is for.</summary>
    public const string Version = "4.3.5";

    private static readonly Dictionary<string, string> Sha256ByAsset = new(StringComparer.Ordinal)
    {
        ["tailwindcss-linux-arm64"] = "ffcd0d21dbd7c605dc12743ff0c0b47debc963c86b399674084c1c8f2c96ecad",
        ["tailwindcss-linux-arm64-musl"] = "564dad3028c029b0c69042c66a7a79873ed7289b73fcb7b37973fbf79cb9a32f",
        ["tailwindcss-linux-x64"] = "e47695cee3f0d90623a91f3939b08bdee610eb5ad99340adff9d4145b284d6cc",
        ["tailwindcss-linux-x64-musl"] = "bb050edd13f15006d872a9bb1772b908618a8df6806d30bf095cba26a09e640b",
        ["tailwindcss-macos-arm64"] = "f3f4f5d1727e2b693b8f1c9a1492b5dd87008c86ea286bee1589cb458e6235fc",
        ["tailwindcss-macos-x64"] = "a63702686a3ed048b3135d84a73be49ea9d47f880c9b20e54b3ef60934ad8cdf",
        ["tailwindcss-windows-x64.exe"] = "2803583268c53f4713e89943e211081fddb186c0a4a7d07b5c342c6ae853ec95",
    };

    /// <summary>The recorded digest of <paramref name="assetName" />, or null for a version Rask does not pin.</summary>
    public static string? For(string version, string assetName) =>
        string.Equals(version, Version, StringComparison.Ordinal)
        && Sha256ByAsset.TryGetValue(assetName, out var digest)
            ? digest
            : null;
}
