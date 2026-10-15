using System;
using System.Collections.Generic;

namespace Rask.TypeScript.Tasks;

/// <summary>
///     The npm integrity of every tool package this task downloads, for the versions Rask pins.
/// </summary>
/// <remarks>
///     <para>
///         The registry's own <c>integrity</c> field comes from the same place as the tarball — and the
///         registry is a settable property — so checking against it catches a corrupted download and
///         nothing else. These are recorded HERE, in the repository, when a pin moves, so the bytes a
///         build is about to execute have to be the ones that were published on the day somebody looked.
///     </para>
///     <para>
///         <c>scripts/upstream/tools.py</c> moves <c>RaskEsbuildVersion</c>, <c>RaskTsgoVersion</c> and
///         <c>RaskExternalTypeScriptVersion</c> with their rows, hashing each tarball it downloads and
///         recording it only when it agrees with the registry's <c>dist.integrity</c>. A test fails while a
///         pinned version has a published package with no entry here.
///     </para>
/// </remarks>
internal static class TypeScriptToolPins
{
    // "<package>@<version>" -> "sha512-<base64>".
    private static readonly Dictionary<string, string> IntegrityByPackage = new(StringComparer.Ordinal)
    {
        ["@esbuild/darwin-x64@0.28.2"] = "sha512-uq6suIWYP37qzGddBKPw5QEQPi6HiLGsO7UmkpfyaYNQ3D+rN6w6WfwH+nuqcGXWvawGwxOEroO4YGnFh95azw==",
        ["@esbuild/darwin-arm64@0.28.2"] = "sha512-n4KqkOQrraxHJcgjM1RvwbigfQKIKJVpM7xp+KsxiyUSrRdIXnt73VhrPAx0fV44hgfmIVKjxMN9J1t5jySVkw==",
        ["@esbuild/linux-x64@0.28.2"] = "sha512-4xTZr1FUmSoQW4XIWmit3tzQrUTZM+N3P0XV8xROKYF50XfI7xeO90+1bZvNwxIufQ9hDQVRJH5YhgPVF8A/HQ==",
        ["@esbuild/linux-arm64@0.28.2"] = "sha512-pW4AC0P3it8c7do9MVM4p51FzHzdM/TZrerurgRcHJ2WTa1VQ1CIq18xncfpBJw4ojkiZZrKW2yIBWBP92j6Ug==",
        ["@esbuild/linux-ia32@0.28.2"] = "sha512-CYbnj78HsIeA+DhgUKgFCfvNsTHFhMMrinUrMZpDXJXKN8T3XViTZ/+wtHeVxEWY8ewSzTFN+nRmSwO2tZaLUQ==",
        ["@esbuild/linux-arm@0.28.2"] = "sha512-XlDnu2q5yoqems+xay6wSAcg9DDD7K9RLKZEBOMZm3ckNpJBvOX20tSfby8KfrrhINDyv9V2YVZKY/SpoGJI8w==",
        ["@esbuild/win32-x64@0.28.2"] = "sha512-5ebpxr3nWMzrL/rnUI755Jkuee0bHL/Gq0WTF9lvcpv73wAp5eu8MfBUgWK9bhWvZjj7yX8etf/8tI8Ney695g==",
        ["@esbuild/win32-arm64@0.28.2"] = "sha512-PIhhEkE9uPBleRBrQEJpUn7MBnibZzbGzYWPmY3x+YoVg/95zbjB4CxPPOQ8l5tYYM4mMaCthF8/1DIfBQQyWQ==",
        ["@esbuild/win32-ia32@0.28.2"] = "sha512-YmJbfTlvU7Sdn9BB+4PRES4oB6pxgS37MAONj+hBr/cpXS1aBPKXxNnDbu+QCWPj0o9dgyxeq79g6c5P8KeuYA==",

        ["@typescript/typescript-darwin-x64@7.0.2"] = "sha512-SZ9xZInqApNlNGc9s0W1VSsktYSOe9cFqNOIqmN1Gs8SmkjKZYFt017G4VwPxASInODuAdbTW7sXiFUf893RgA==",
        ["@typescript/typescript-darwin-arm64@7.0.2"] = "sha512-gowzar9MwS/aRWp6f3a4KUqzRjAZjOsmGNCM6LcTgXum+dBfgsBVMN+AgvOCCbguXyick6LJhpBszxMebJ8syA==",
        ["@typescript/typescript-linux-x64@7.0.2"] = "sha512-EYdf2cNg7rgCWJnxCdJ+F3V39O8ihb37eHAu1LK8oAFizgTQbPOK7zHHXbPt8rX24COqODXeI3sIf0fCXG7H/A==",
        ["@typescript/typescript-linux-arm64@7.0.2"] = "sha512-Qh4eU4/y3yDjnfjjyPYihMj5/ODIlmt+Bzu17OI+fiSRDW57QmU5SiN63exPRNJPKUzcc1INa1NXdrJ+MqHjUQ==",
        ["@typescript/typescript-win32-x64@7.0.2"] = "sha512-0BQ3HkAHHlKLSp1qRvf3SUhGpGsDuhB/jgFw75guyqbxJqEaS0Cw/VFO8i2nHglJUzQCRtMMR/IBAKE3ETMC4g==",
        ["@typescript/typescript-win32-arm64@7.0.2"] = "sha512-Gyl1Vy6OsWesLzmq+EP0Fb7b4Nid5232AvcA2SFcdYreldpNtYFFofPjnt62y9hQy7VTaZp65ICJjuAQRaVcIQ==",

        ["typescript@6.0.3"] = "sha512-y2TvuxSZPDyQakkFRPZHKFm+KKVqIisdg9/CZwm9ftvKXLP8NRWj38/ODjNbr43SsoXqNuAisEf1GdCxqWcdBw==",
    };

    /// <summary>The recorded integrity of <paramref name="packageName" /> at <paramref name="version" />, or null when Rask does not pin it.</summary>
    public static string? For(string packageName, string version) =>
        IntegrityByPackage.TryGetValue(packageName + "@" + version, out var integrity) ? integrity : null;
}
