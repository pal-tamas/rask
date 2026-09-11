namespace Rask.Hosting.Shared;

/// <summary>
///     Where the <c>https://&lt;name&gt;.test</c> dev host keeps what it made, and the port it serves on.
/// </summary>
/// <remarks>
///     <para>
///         <c>rask dev</c> writes these (the certificate for each hostname, under <c>~/.rask</c>) and an app
///         launched by an editor reads them back, so a project that <c>rask dev</c> has set up once is served
///         on the same name under F5. Two definitions would be two chances for the name and the certificate
///         to stop agreeing, which the browser reports as an untrusted site rather than as a path mismatch.
///     </para>
///     <para>
///         The port is the one platform difference an app has to know: macOS reserves ports below 1024, so
///         Kestrel listens on a high port there and a pf redirect carries 443 to it; Windows and Linux let
///         the app bind 443 itself.
///     </para>
/// </remarks>
internal static class DevHostPaths
{
    /// <summary>The port Kestrel listens on where an unprivileged process may bind 443.</summary>
    internal const int DirectHttpsPort = 443;

    /// <summary>The port Kestrel listens on behind macOS's pf redirect from 443.</summary>
    internal const int RedirectedHttpsPort = 5001;

    /// <summary>The default root: <c>~/.rask</c>.</summary>
    internal static string DefaultRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".rask");

    /// <summary>Where certificates and keys live under <paramref name="root" />.</summary>
    internal static string CertificateDirectory(string root) => Path.Combine(root, "certs");

    /// <summary>The PEM certificate issued for <paramref name="hostname" />.</summary>
    internal static string CertificatePath(string root, string hostname) =>
        Path.Combine(CertificateDirectory(root), hostname + ".pem");

    /// <summary>The PEM private key for <paramref name="hostname" />'s certificate.</summary>
    internal static string KeyPath(string root, string hostname) =>
        Path.Combine(CertificateDirectory(root), hostname + ".key");

    /// <summary>The port Kestrel listens on for the dev host on this operating system.</summary>
    internal static int HttpsPort => OperatingSystem.IsMacOS() ? RedirectedHttpsPort : DirectHttpsPort;
}
