using System.Globalization;
using System.Text;

namespace Rask.Hosting.Shared;

/// <summary>
///     Turns a project name into the hostname <c>rask dev</c> serves it on — <c>AppName</c> becomes
///     <c>appname.test</c>.
/// </summary>
/// <remarks>
///     <para>
///         The suffix is <c>.test</c> and is deliberately not configurable. RFC 6761 §6.2 reserves it
///         for exactly this and guarantees it will never be delegated in the real DNS root, so a name
///         here can never collide with a site the developer actually needs to reach. The two obvious
///         alternatives are both traps, which is the reason this is a constant rather than a setting:
///         <c>.local</c> is claimed by multicast DNS (RFC 6762) and on macOS is answered by
///         <c>mDNSResponder</c> rather than <c>/etc/hosts</c>, and <c>.dev</c> is a real gTLD on the
///         HSTS preload list — which is what forced Valet off it when Chrome began force-upgrading
///         every <c>.dev</c> to HTTPS. A knob here would only let someone pick one of those.
///     </para>
///     <para>
///         The derivation is pinned by tests so the same project always resolves to the same name,
///         which is what lets the hosts entry, the certificate and the browser tab all agree without
///         anyone writing the name down.
///     </para>
/// </remarks>
internal static class DevHostName
{
    /// <summary>The suffix every dev hostname ends in. See the remarks on this class for why it is fixed.</summary>
    public const string Suffix = "test";

    /// <summary>The longest a single DNS label may be (RFC 1035).</summary>
    private const int MaxLabelLength = 63;

    /// <summary>
    ///     The hostname for <paramref name="projectName" />, or null when nothing usable survives the
    ///     slug (a project named entirely in a script with no ASCII fold, say). Null means "serve on
    ///     localhost as before" — never an error, because failing to invent a nicer URL must not stop
    ///     the app from running.
    /// </summary>
    public static string? From(string? projectName)
    {
        var label = Slug(projectName);
        return label is null ? null : label + "." + Suffix;
    }

    /// <summary>
    ///     The DNS label for a project name: lower-cased ASCII, everything else collapsed to a single
    ///     dash. <c>My.App.Web</c> and <c>My App Web</c> both become <c>my-app-web</c>.
    /// </summary>
    /// <remarks>
    ///     Dots collapse to dashes rather than surviving: a project called <c>Contoso.Web</c> would
    ///     otherwise become <c>contoso.web.test</c>, a three-label name that reads like a subdomain and
    ///     needs its own hosts entry for no benefit. One project, one label.
    /// </remarks>
    public static string? Slug(string? projectName)
    {
        if (string.IsNullOrWhiteSpace(projectName))
        {
            return null;
        }

        // Decomposed first so an accented letter contributes its base character instead of being
        // dropped whole — "Café" is "cafe", not "caf".
        var normalized = projectName.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        var pendingDash = false;

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsAsciiLetterOrDigit(character))
            {
                // The dash is only emitted once something follows it, so a leading run of separators
                // never reaches the output and a trailing one never has to be trimmed off again.
                if (pendingDash && builder.Length > 0)
                {
                    builder.Append('-');
                }

                pendingDash = false;
                builder.Append(char.ToLowerInvariant(character));
                continue;
            }

            pendingDash = true;
        }

        if (builder.Length == 0)
        {
            return null;
        }

        // A label must also start with a letter or digit, which the loop above already guarantees, and
        // stay inside 63 characters, which it does not.
        return builder.Length <= MaxLabelLength
            ? builder.ToString()
            : builder.ToString(0, MaxLabelLength).TrimEnd('-');
    }
}
