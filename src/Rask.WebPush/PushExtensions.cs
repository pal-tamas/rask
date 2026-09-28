namespace Rask.WebPush;

/// <summary>The same verbs on an injected <see cref="IPush" />.</summary>
public static class PushExtensions
{
    extension(IPush push)
    {
        /// <summary>Sends to every subscriber — or, with <see cref="Pushing.To" />, to one user's browsers.</summary>
        public Pushing Send(WebPushMessage message, CancellationToken cancellationToken = default) =>
#pragma warning disable CA2208 // CA2208 cannot see a C# 14 extension receiver
            new(push ?? throw new ArgumentNullException(nameof(push)), message ?? throw new ArgumentNullException(nameof(message)), null, cancellationToken);
#pragma warning restore CA2208
    }
}
