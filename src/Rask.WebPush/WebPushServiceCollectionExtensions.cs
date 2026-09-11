using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rask.Hosting.Shared;

namespace Rask.WebPush;

// DI entry point. Call once at startup:
//
//   builder.Services.AddRaskWebPush();
//
// with the key pair and contact in configuration (the keys in user-secrets or the environment, never in source):
//
//   "Rask": { "WebPush": { "Subject": "mailto:admin@example.com",
//                          "VapidKeys": { "PublicKey": "…", "PrivateKey": "…" } } }
//
// then inject IWebPush wherever you deliver notifications.
/// <summary>Registers the Web Push sender.</summary>
public static class WebPushServiceCollectionExtensions
{
    /// <summary>
    ///     Registers <see cref="IWebPush" /> and its options. Call once at startup, then inject the
    ///     sender wherever notifications are delivered:
    ///     <code>
    ///     builder.Services.AddRaskWebPush();   // Rask:WebPush:VapidKeys + Rask:WebPush:Subject
    ///     </code>
    ///     <para>
    ///         <see cref="WebPushOptions" /> reads the <c>Rask:WebPush</c> configuration section first and then
    ///         <paramref name="configure" />, so code wins. The options are validated when the host starts, so a
    ///         missing key pair or a malformed subject fails there rather than on the first notification nobody
    ///         receives. Idempotent: the first call's options win.
    ///     </para>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Adjusts the options, after the <c>Rask:WebPush</c> section.</param>
    /// <returns><paramref name="services" />, for chaining.</returns>
    public static IServiceCollection AddRaskWebPush(
        this IServiceCollection services,
        Action<WebPushOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // A second call registers nothing — before, it added a second options instance and a second typed client.
        if (!services.AddRaskOptions<WebPushOptions>(
                "Rask:WebPush", static (section, o) => section.Bind(o), configure, static o => o.Validate()))
        {
            return services;
        }

        // Typed client: IHttpClientFactory supplies the HttpClient; WebPushOptions + the optional
        // ILogger resolve from DI.
        services.AddHttpClient<IWebPush, WebPushSender>();
        return services;
    }
}
