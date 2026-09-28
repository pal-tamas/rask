namespace Rask.Mailing;

/// <summary>Transport security for the SMTP connection.</summary>
public enum SmtpSecurity
{
    /// <summary>Let MailKit choose based on the port (STARTTLS on 587, implicit TLS on 465). The default.</summary>
    Auto,

    /// <summary>Connect in the clear then upgrade with STARTTLS (typically port 587).</summary>
    StartTls,

    /// <summary>Implicit TLS from connect (typically port 465).</summary>
    SslOnConnect,

    /// <summary>No transport encryption (development / a local relay only).</summary>
    None,
}
