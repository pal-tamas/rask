namespace Rask.Mailing;

/// <summary>Transport security for the SMTP connection.</summary>
public enum SmtpSecurity
{
    /// <summary>
    /// TLS by the port: implicit TLS on 465, a required STARTTLS on any other — a server that does not offer it is refused,
    /// never written to in the clear. The default.
    /// </summary>
    Auto,

    /// <summary>Connect in the clear then upgrade with STARTTLS (typically port 587).</summary>
    StartTls,

    /// <summary>Implicit TLS from connect (typically port 465).</summary>
    SslOnConnect,

    /// <summary>No transport encryption (development / a local relay only).</summary>
    None,
}
