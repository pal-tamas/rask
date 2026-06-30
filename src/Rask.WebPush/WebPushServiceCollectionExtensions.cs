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
// then inject IWebPushSender wherever you deliver notifications.
public static class WebPushServiceCollectionExtensions
{
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
        services.AddHttpClient<IWebPushSender, WebPushSender>();
        return services;
    }
}
