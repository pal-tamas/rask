using MailKit.Security;

namespace Rask.Mailing.Tests;

/// <summary>What each <see cref="SmtpSecurity" /> asks of the connection.</summary>
public sealed class SmtpSecurityTests
{
    [Theory]
    [InlineData(587)]
    [InlineData(25)]
    [InlineData(2525)]
    public void Auto_requires_starttls_off_the_implicit_tls_port(int port)
    {
        var socket = MailKitSender.ToSocketOptions(SmtpSecurity.Auto, port);

        Assert.Equal(SecureSocketOptions.StartTls, socket);
    }

    [Fact]
    public void Auto_speaks_tls_from_the_start_on_port_465()
    {
        var socket = MailKitSender.ToSocketOptions(SmtpSecurity.Auto, 465);

        Assert.Equal(SecureSocketOptions.SslOnConnect, socket);
    }

    [Fact]
    public void Only_none_sends_in_the_clear()
    {
        var socket = MailKitSender.ToSocketOptions(SmtpSecurity.None, 25);

        Assert.Equal(SecureSocketOptions.None, socket);
    }
}
