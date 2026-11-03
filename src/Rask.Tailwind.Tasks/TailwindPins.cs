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
    public const string Version = "4.3.4";

    private static readonly Dictionary<string, string> Sha256ByAsset = new(StringComparer.Ordinal)
    {
        ["tailwindcss-linux-arm64"] = "f6d7baae1d350ed3a2defe98d0b5cc58e37400c0173b2e38d42f690ccc4bbfe1",
        ["tailwindcss-linux-arm64-musl"] = "64853a5d1321b9622eeceb16e28d26889fca409e3b1a2f95453088f776461e44",
        ["tailwindcss-linux-x64"] = "149a8ece5b855587a9c10de6da9dcbcfad0e47c4ed5dca219f721b3eae39d817",
        ["tailwindcss-linux-x64-musl"] = "2a5456987c41ce3311e423b603842af425ea5ac44505162dd348d43ed3b909fd",
        ["tailwindcss-macos-arm64"] = "a516a11b439bfbb5ecd4a2d13b75e6cdcc284dc710aed8731e31d2e5c03895a4",
        ["tailwindcss-macos-x64"] = "7ec771593a1fb8bbd204f70429361b0158f7989e4136bc68d04513bf543a15ba",
        ["tailwindcss-windows-x64.exe"] = "ad84612696f3be102fa005d4827f0df84c7b4570d31c06865ce5fde0311478af",
    };

    /// <summary>The recorded digest of <paramref name="assetName" />, or null for a version Rask does not pin.</summary>
    public static string? For(string version, string assetName) =>
        string.Equals(version, Version, StringComparison.Ordinal)
        && Sha256ByAsset.TryGetValue(assetName, out var digest)
            ? digest
            : null;
}
