using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Rask.Mail;

/// <summary>Registers transactional email into an <see cref="IServiceCollection"/>.</summary>
public static class RaskMailServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="IMailQueue"/>, an <see cref="IMailSender"/> (SMTP via MailKit when
    /// <see cref="MailOptions.Smtp"/> is set, else an <c>.eml</c> pickup directory, else logging), and the
    /// background <see cref="MailProcessor{TContext}"/>. Map the table with <c>modelBuilder.AddRaskMail()</c>
    /// in <c>OnModelCreating</c> and register your context as an <see cref="IDbContextFactory{TContext}"/>.
    /// To use a custom sender, register your own <see cref="IMailSender"/> (at any lifetime — the processor
    /// resolves it per message from a scope) before calling this. Calling this more than once registers a
    /// single processor and keeps the <b>first</b> call's options.
    /// </summary>
    /// <typeparam name="TContext">The application <see cref="DbContext"/> that owns the mail table.</typeparam>
    public static IServiceCollection AddRaskMail<TContext>(this IServiceCollection services, Action<MailOptions>? configure = null)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new MailOptions();
        configure?.Invoke(options);
        options.Validate();

        services.TryAddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IMailSender>(sp => CreateSender(sp, options));
        services.TryAddSingleton<IMailQueue, MailQueue<TContext>>();

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
