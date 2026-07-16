using Microsoft.Extensions.DependencyInjection;

namespace Rask.Mail.Tests;

public sealed class MailUnitTests
{
    [Fact]
    public void RetryDelay_grows_exponentially_and_caps()
    {
        var options = new MailOptions
        {
            From = "x@example.com",
            BaseRetryDelay = TimeSpan.FromSeconds(10),
            MaxRetryDelay = TimeSpan.FromMinutes(1),
        };

        Assert.Equal(TimeSpan.FromSeconds(10), options.RetryDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(20), options.RetryDelay(2));
        Assert.Equal(TimeSpan.FromSeconds(40), options.RetryDelay(3));
        Assert.Equal(TimeSpan.FromMinutes(1), options.RetryDelay(4));   // 80s capped to 60s
        Assert.Equal(TimeSpan.FromMinutes(1), options.RetryDelay(99));  // no overflow
    }

    [Fact]
    public void AddRaskMail_validates_options_eagerly()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentException>(() => services.AddRaskMail<MailDbContext>(o => o.From = ""));
    }

    [Fact]
    public void Builder_rejects_a_malformed_address_at_the_call_site()
    {
        Assert.Throws<ArgumentException>(() => Email.To("not a valid address"));
        Assert.Throws<ArgumentException>(() => Email.To("ada@example.com").Cc("also bad"));
    }

    [Fact]
    public void ToQueuedMail_requires_a_body()
    {
        var email = Email.To("ada@example.com").Subject("No body");
        Assert.Throws<ArgumentException>(() =>
            MailSerializer.ToQueuedMail(email, new EmailAddress("from@example.com"), DateTime.UtcNow, DateTime.UtcNow));
    }

    [Fact]
    public void Serializer_round_trips_the_full_envelope()
    {
        var email = Email
            .To("ada@example.com", "Ada")
            .AndTo("grace@example.com")
            .Cc("carol@example.com")
            .ReplyTo("support@example.com", "Support")
            .Subject("Report")
            .Body("<p>body</p>")
            .PlainText("body")
            .Attach("report.txt", "text/plain", [1, 2, 3]);

        var row = MailSerializer.ToQueuedMail(email, new EmailAddress("noreply@example.com", "Example"), DateTime.UtcNow, DateTime.UtcNow);
        var outgoing = MailSerializer.ToOutgoing(row);

        Assert.Equal("noreply@example.com", outgoing.From.Address);
        Assert.Equal("Example", outgoing.From.Name);
        Assert.Equal(["ada@example.com", "grace@example.com"], outgoing.To.Select(a => a.Address));
        Assert.Equal("Ada", outgoing.To[0].Name);
        Assert.Equal("carol@example.com", Assert.Single(outgoing.Cc).Address);
        Assert.Empty(outgoing.Bcc);
        Assert.Equal("support@example.com", outgoing.ReplyTo!.Address);
        Assert.Equal("Report", outgoing.Subject);
        Assert.Equal("<p>body</p>", outgoing.HtmlBody);
        Assert.Equal("body", outgoing.TextBody);
        var attachment = Assert.Single(outgoing.Attachments);
        Assert.Equal("report.txt", attachment.FileName);
        Assert.Equal([1, 2, 3], attachment.Content);
    }

    [Fact]
    public async Task PickupDirectoryMailSender_writes_an_eml_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"rask-mail-pickup-{Guid.NewGuid():N}");
        try
        {
            var sender = new PickupDirectoryMailSender(new MailOptions { From = "x@example.com", PickupDirectory = dir });
            await sender.SendAsync(new OutgoingMail
            {
                From = new EmailAddress("noreply@example.com"),
                To = [new EmailAddress("ada@example.com")],
                Subject = "Hello",
                HtmlBody = "<p>hi</p>",
            });

            var file = Assert.Single(Directory.GetFiles(dir, "*.eml"));
            var contents = await File.ReadAllTextAsync(file);
            Assert.Contains("Subject: Hello", contents);
            Assert.Contains("ada@example.com", contents);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
