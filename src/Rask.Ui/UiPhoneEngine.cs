namespace Rask;

/// <summary>
///     Where the phone engine is served from: the script the runtime's hook loads (<c>data-rask-phone</c>), and
///     only on a page that draws a <see cref="UiPhone" />.
/// </summary>
/// <remarks>
///     The engine is intl-tel-input, the library fluxui.dev's phone field runs, with Google's libphonenumber in a
///     second file it fetches when the first field mounts. Neither is in Rask's runtime nor in this assembly: the
///     app's build copies both into <c>wwwroot/js</c> (<c>build/Rask.Ui.targets</c>, <c>RaskUiPhoneEngine</c>).
///     <c>Resources/phone/build.mjs</c> builds them and writes the two versions.
/// </remarks>
internal static class UiPhoneEngine
{
    /// <summary>The path the build copies the engine to, under the app's web root.</summary>
    public const string Path = "/js/rask-ui-phone.js";

    /// <summary>The first eight hex digits of the engine's SHA-256: the URL changes when the bytes do.</summary>
    public const string Version = "f90d8c05";

    /// <summary>The same for the utils, which the engine asks for beside itself.</summary>
    public const string UtilsVersion = "12d617d0";

    /// <summary>The engine's URL under <paramref name="pathBase" />, carrying its version.</summary>
    public static string Href(string? pathBase = null) => (pathBase ?? string.Empty) + Path + "?v=" + Version;
}
