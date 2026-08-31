using Microsoft.Extensions.DependencyInjection;

namespace Rask.WebPush;

// DI entry point. Call once at startup:
//
//   builder.Services.AddRaskWebPush(o =>
//   {
//       o.VapidKeys = VapidKeys.Generate();      // generate once, then load from config/secrets
//       o.Subject   = "mailto:admin@example.com";
//   });
//
// then inject IWebPush wherever you deliver notifications.
/// <summary>Registers the Web Push sender.</summary>
public static class WebPushServiceCollectionExtensions
{
    /// <summary>
    ///     Registers <see cref="IWebPush" /> and its options. Call once at startup, then inject the
    ///     sender wherever notifications are delivered:
    ///     <code>
    ///     builder.Services.AddRaskWebPush(o =>
    ///     {
    ///         o.VapidKeys = builder.Configuration.GetSection("WebPush").Get&lt;VapidKeys&gt;();
    ///         o.Subject   = "mailto:admin@example.com";
    ///     });
    ///     </code>
    ///     <para>
    ///         The options are validated here, so a missing key pair or a malformed subject fails at
    ///         startup rather than on the first notification nobody receives.
    ///     </para>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Sets the VAPID keys and contact subject — both required.</param>
    /// <returns><paramref name="services" />, for chaining.</returns>
    /// <exception cref="InvalidOperationException">The resulting options cannot send. See
    ///     <see cref="WebPushOptions.Validate" />.</exception>
    public static IServiceCollection AddRaskWebPush(this IServiceCollection services, Action<WebPushOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new WebPushOptions();
        configure(options);
        options.Validate(); // fail fast at startup rather than on the first send.

        services.AddSingleton(options);
        // Typed client: IHttpClientFactory supplies the HttpClient; WebPushOptions + the optional
        // ILogger resolve from DI.
        services.AddHttpClient<IWebPush, WebPushSender>();
        return services;
    }
}
