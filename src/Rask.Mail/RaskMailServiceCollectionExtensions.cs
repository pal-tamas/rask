using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Rask.Hosting.Shared;

namespace Rask.Mail;

/// <summary>Registers transactional email into an <see cref="IServiceCollection"/>.</summary>
public static class RaskMailServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="IMail"/>, an <see cref="IMailSender"/> (SMTP via MailKit when
    /// <see cref="MailOptions.Smtp"/> is set, else an <c>.eml</c> pickup directory, else logging), and the
    /// background <see cref="MailProcessor{TContext}"/>. Map the table with <c>modelBuilder.AddRaskMail()</c>
    /// in <c>OnModelCreating</c> and register your context as an <see cref="IDbContextFactory{TContext}"/>.
    /// <see cref="MailOptions"/> reads the <c>Rask:Mail</c> configuration section first and then
    /// <paramref name="configure"/>, so code wins — any <c>Rask:Mail:Smtp</c> key turns SMTP on, which is how a
    /// deployed app sends real mail without its password in source.
    /// To use a custom sender, register your own <see cref="IMailSender"/> (at any lifetime — the processor
    /// resolves it per message from a scope) before calling this. Calling this more than once registers a
    /// single processor and keeps the <b>first</b> call's options.
    /// </summary>
    /// <typeparam name="TContext">The application <see cref="DbContext"/> that owns the mail table.</typeparam>
    public static IServiceCollection AddRaskMail<TContext>(this IServiceCollection services, Action<MailOptions>? configure = null)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddRaskOptions<MailOptions>("Rask:Mail", static (section, o) => section.Bind(o), configure,
            static o => o.Validate());
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<MailMetrics>();

        // Chosen from the BUILT options: whether SMTP is configured can come from Rask:Mail:Smtp, which is not
        // readable until the container is.
        services.TryAddSingleton<IMailSender>(static sp => CreateSender(sp, sp.GetRequiredService<MailOptions>()));
        services.TryAddSingleton<IMail, MailQueue<TContext>>();

        // Before the processor, so an app whose model never mapped QueuedMail fails the boot with the
        // line to type rather than on the first password reset. The processor itself tolerates a missing
        // table — it has to, because a freshly scaffolded app boots before its first migration has run —
        // so it is the wrong place to notice. See BatteryModelCheck: this reads the MODEL, never the
        // database, so an app that has not run `rask db update` yet still starts.
        services.AddHostedService<MailModelCheck<TContext>>();

        // AddHostedService uses TryAddEnumerable, so a repeated call registers only one processor.
        services.AddHostedService<MailProcessor<TContext>>();
        return services;
    }

    private static IMailSender CreateSender(IServiceProvider sp, MailOptions options)
    {
        if (options.Smtp is not null)
        {
            return new MailKitMailSender(options);
        }

        if (options.PickupDirectory is not null)
        {
            return new PickupDirectoryMailSender(options);
        }

        return new LogMailSender(sp.GetRequiredService<ILogger<LogMailSender>>());
    }
}
