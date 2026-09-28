namespace Rask.Mailing;

/// <summary>SMTP server connection settings.</summary>
public sealed class SmtpOptions
{
    /// <summary>The SMTP server host name.</summary>
    public string Host { get; set; } = "";

    /// <summary>The SMTP server port. Default 587 (submission).</summary>
    public int Port { get; set; } = 587;

    /// <summary>The user name for SMTP authentication, or <c>null</c> to connect unauthenticated.</summary>
    public string? User { get; set; }

    /// <summary>The password for SMTP authentication.</summary>
    public string? Password { get; set; }

    /// <summary>The transport security mode. Default <see cref="SmtpSecurity.Auto"/>.</summary>
    public SmtpSecurity Security { get; set; } = SmtpSecurity.Auto;
}
