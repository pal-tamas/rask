using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Mail.Tests;

// MailOptions come from Rask:Mail first and the AddRaskMail callback second — and the sender is chosen from the built
// options, so SMTP configured only in appsettings or the environment is what delivers.
public sealed class RaskMailOptionsBindingTests
{
    [Fact]
    public void The_Rask_Mail_section_sets_the_from_address_and_the_pickup_directory()
    {
        using var provider = Provider(new()
        {
            ["Rask:Mail:From"] = "hello@example.test",
            ["Rask:Mail:PickupDirectory"] = "outbox-eml",
        });

        var options = provider.GetRequiredService<MailOptions>();

        Assert.Equal("hello@example.test", options.From);
        Assert.Equal("outbox-eml", options.PickupDirectory);
        Assert.IsType<PickupDirectoryMailSender>(provider.GetRequiredService<IMailSender>());
    }

    [Fact]
    public void An_Smtp_section_in_configuration_switches_delivery_to_smtp()
    {
        using var provider = Provider(new()
        {
            ["Rask:Mail:From"] = "hello@example.test",
            ["Rask:Mail:PickupDirectory"] = "outbox-eml",
            ["Rask:Mail:Smtp:Host"] = "smtp.example.test",
            ["Rask:Mail:Smtp:Port"] = "2525",
        });

        var options = provider.GetRequiredService<MailOptions>();

        Assert.Equal("smtp.example.test", options.Smtp!.Host);
        Assert.Equal(2525, options.Smtp.Port);
        Assert.IsType<MailKitMailSender>(provider.GetRequiredService<IMailSender>());
    }

    [Fact]
    public void The_callback_wins_over_the_section()
    {
        using var provider = Provider(
            new() { ["Rask:Mail:From"] = "from-config@example.test" },
            o => o.From = "from-code@example.test");

        Assert.Equal("from-code@example.test", provider.GetRequiredService<MailOptions>().From);
    }

    [Fact]
    public void A_top_level_Mail_section_is_not_read()
    {
        using var provider = Provider(
            new() { ["Mail:PickupDirectory"] = "old-place" },
            o => o.From = "hello@example.test");

        Assert.Null(provider.GetRequiredService<MailOptions>().PickupDirectory);
    }

    private static ServiceProvider Provider(Dictionary<string, string?> settings, Action<MailOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddRaskMail<MailDbContext>(configure);
        return services.BuildServiceProvider();
    }
}
