namespace Rask.Mailing;

/// <summary>The timing steps on an injected <see cref="IMail" />, worded as on <see cref="Mail" />.</summary>
public static class MailExtensions
{
    extension(IMail mail)
    {
        /// <summary>Queues <paramref name="email" /> to go out as soon as the processor next polls.</summary>
        public Sending Send(Email email, CancellationToken cancellationToken = default) =>
#pragma warning disable CA2208 // CA2208 cannot see a C# 14 extension receiver
            new(mail ?? throw new ArgumentNullException(nameof(mail)), email, null, null, cancellationToken);
#pragma warning restore CA2208
    }
}
