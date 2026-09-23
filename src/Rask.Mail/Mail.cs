using Microsoft.Extensions.DependencyInjection;

namespace Rask.Mail;

/// <summary>
///     The app's outgoing mail, with nothing injected — from a handler, a render, a request, a job:
/// </summary>
/// <remarks>
///     <code>
///     await Mail.Send(Email.To(user.Email).Subject("Welcome").Body(new WelcomeEmail(user.Name)));
///     await Mail.Send(reminder).In(24.Hours);
///     await Mail.Send(digest).At(mondayMorning);
///     </code>
///     <para>
///         Each call reaches the <see cref="IMail" /> of the work it runs in and is cancelled with that work.
///         Outside any — a hosted service, a timer started at boot — it throws; inject <see cref="IMail" />
///         there instead.
///     </para>
/// </remarks>
public static class Mail
{
    /// <summary>Queues <paramref name="email" /> to go out as soon as the processor next polls.</summary>
    public static Sending Send(Email email, CancellationToken cancellationToken = default) =>
        new(null, email, null, null, cancellationToken);

    /// <summary>What <c>Mail.Fake()</c> put in the way of the real battery, for this test's flow alone.</summary>
    internal static readonly AsyncLocal<IMail?> Faked = new();

    internal static IMail Resolve()
    {
        if (Faked.Value is { } fake)
        {
            return fake;
        }

        var services = Ambient.Services
            ?? throw new InvalidOperationException(
                "Mail was called outside any work in progress — a handler, a render, a request or a job — so "
                + "there is no app to reach. Inject IMail in the constructor there instead.");

        return services.GetService<IMail>()
            ?? throw new InvalidOperationException(
                "Mail needs Rask.Mail registered: call builder.Services.AddRaskMail<AppDbContext>().");
    }
}

/// <summary>The timing steps on an injected <see cref="IMail" />, worded as on <see cref="Mail" />.</summary>
public static class MailExtensions
{
    extension(IMail mail)
    {
        /// <summary>Queues <paramref name="email" /> to go out as soon as the processor next polls.</summary>
        public Sending Send(Email email, CancellationToken cancellationToken = default) =>
            new(mail ?? throw new ArgumentNullException(nameof(mail)), email, null, null, cancellationToken);
    }
}
