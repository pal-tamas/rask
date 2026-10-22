using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace Rask.Printing;

/// <summary>
/// What a Chromium that would not start said, as the fix: a missing library named with the Ubuntu package that ships
/// it, a sandbox the host forbids named with the setting that turns it off.
/// </summary>
internal static partial class ChromiumStartFailure
{
    // The headless shell's own deb.deps, by the file the loader looks for; package names as Ubuntu 24.04 ships them.
    private static readonly FrozenDictionary<string, string> Packages = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["libatk-bridge-2.0.so.0"] = "libatk-bridge2.0-0t64",
        ["libatk-1.0.so.0"] = "libatk1.0-0t64",
        ["libatspi.so.0"] = "libatspi2.0-0t64",
        ["libglib-2.0.so.0"] = "libglib2.0-0t64",
        ["libgobject-2.0.so.0"] = "libglib2.0-0t64",
        ["libgio-2.0.so.0"] = "libglib2.0-0t64",
        ["libnss3.so"] = "libnss3",
        ["libnssutil3.so"] = "libnss3",
        ["libsmime3.so"] = "libnss3",
        ["libnspr4.so"] = "libnspr4",
        ["libplc4.so"] = "libnspr4",
        ["libplds4.so"] = "libnspr4",
        ["libdbus-1.so.3"] = "libdbus-1-3",
        ["libX11.so.6"] = "libx11-6",
        ["libxcb.so.1"] = "libxcb1",
        ["libXcomposite.so.1"] = "libxcomposite1",
        ["libXdamage.so.1"] = "libxdamage1",
        ["libXext.so.6"] = "libxext6",
        ["libXfixes.so.3"] = "libxfixes3",
        ["libXrandr.so.2"] = "libxrandr2",
        ["libgbm.so.1"] = "libgbm1",
        ["libexpat.so.1"] = "libexpat1",
        ["libxkbcommon.so.0"] = "libxkbcommon0",
        ["libasound.so.2"] = "libasound2t64",
        ["libcups.so.2"] = "libcups2t64",
        ["libudev.so.1"] = "libudev1",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>The cause in <paramref name="log"/>, Chromium's last lines on stderr, or null when it names none.</summary>
    public static string? Explain(IEnumerable<string> log)
    {
        foreach (var line in log)
        {
            if (MissingLibrary().Match(line) is { Success: true } missing)
            {
                return Library(missing.Groups["library"].Value);
            }

            if (line.Contains("No usable sandbox", StringComparison.Ordinal))
            {
                return "Chromium cannot start its sandbox: this host restricts the user namespaces it needs (Ubuntu 24.04 "
                    + "and later do, through AppArmor). Inside a container whose host restricts user namespaces, set "
                    + "Rask:Pdf:NoSandbox=true (Rask__Pdf__NoSandbox).";
            }
        }

        return null;
    }

    private static string Library(string library) =>
        Packages.TryGetValue(library, out var package)
            ? $"Chromium cannot start: {library} is missing. Install it in the image: apt-get install {package} (Ubuntu 24.04 and later)."
            : $"Chromium cannot start: {library} is missing. Install the package that provides {library}.";

    [GeneratedRegex(@"error while loading shared libraries: (?<library>[^:\s]+):", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex MissingLibrary();
}
