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
///         <b>Bumping <c>RaskTailwindVersion</c> means replacing this table</b> from that release's
///         <c>sha256sums.txt</c> (the <c>check-dependency-updates</c> skill). A test fails the build's
///         tests while the pinned version has an asset with no entry here.
///     </para>
/// </remarks>
internal static class TailwindPins
{
    /// <summary>The version the table below is for.</summary>
    public const string Version = "4.3.3";

    private static readonly Dictionary<string, string> Sha256ByAsset = new(StringComparer.Ordinal)
    {
        ["tailwindcss-linux-arm64"] = "55fd0b241214eff3de1e8ee4f22796662f2d2e7a49bcfca7477cfd0bac398195",
        ["tailwindcss-linux-arm64-musl"] = "71ea4be79c9de9827545682df3e040053fb535d37c71ed2cfdedf9385a0868e0",
        ["tailwindcss-linux-x64"] = "dc61b3ac6b8c9ca874c0cc4c57b2409791a64c5540404ca5f5367360babc313a",
        ["tailwindcss-linux-x64-musl"] = "a04d34ceacc8f52cbe8920ad846cdeb61d3d0021dba32db0d1f77c9d9fad7a6c",
        ["tailwindcss-macos-arm64"] = "cdf646702987a743464dff4d9c60fd4480d1c1e73dd819a9a67f1078815dce9d",
        ["tailwindcss-macos-x64"] = "7922e0953f2110c05976e3bf58f14e643d90427575e766b7d433f5f80cbee7e1",
        ["tailwindcss-windows-x64.exe"] = "e0e260ce048014e9268f6237ff18f8ccf02cef521cbd0ae04e82c2cdf7aa3955",
    };

    /// <summary>The recorded digest of <paramref name="assetName" />, or null for a version Rask does not pin.</summary>
    public static string? For(string version, string assetName) =>
        string.Equals(version, Version, StringComparison.Ordinal)
        && Sha256ByAsset.TryGetValue(assetName, out var digest)
            ? digest
            : null;
}
