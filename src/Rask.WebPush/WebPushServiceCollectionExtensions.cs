using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rask.Batteries;
using Rask.Hosting.Shared;

namespace Rask.WebPush;

/// <summary>Registers Web Push.</summary>
public static class WebPushServiceCollectionExtensions
{
    /// <summary>
    /// The sender alone — <see cref="IWebPush" />, one subscription at a time — for an app that keeps its
    /// subscriptions somewhere of its own. Reads <c>Rask:WebPush</c> and refuses to start without a key pair and a contact.
    /// </summary>
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

    /// <summary>
    /// The battery: subscribers kept on <typeparamref name="TContext" /> (map the table with
    /// <c>modelBuilder.AddRaskWebPush()</c>), <c>Push.Send(message)</c> to reach them, and <see cref="IPush" />.
    /// </summary>
    /// <remarks>
    /// Unlike the sender alone this starts without a key pair: a fresh clone of a scaffolded app has none (the
    /// development pair lives in a gitignored file), and the subscribe endpoints and the table have to work before
    /// anybody sends. Sending is what needs the keys, and it says so, naming the settings.
    /// </remarks>
    public static IServiceCollection AddRaskWebPush<TContext>(
        this IServiceCollection services,
        Action<WebPushOptions>? configure = null)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        // Validated at the first send (WebPushSender checks its options when it is built), not at start. An app that
        // also called AddRaskWebPush() first keeps that call's start-time validation: the options are registered once.
        if (services.AddRaskOptions<WebPushOptions>(
                "Rask:WebPush", static (section, o) => section.Bind(o), configure, validate: null))
        {
            services.AddHttpClient<IWebPush, WebPushSender>();
        }

        services.TryAddSingleton(Clock.TimeProvider); // Rask's clock, so Clock.Fake moves this battery's time too
        services.TryAddSingleton<IPush, PushStore<TContext>>();

        // Before anything sends, so an app whose model never mapped PushSubscriber fails the boot naming the line
        // to type, rather than on the first subscription. Reads the MODEL, never the database, so an app that has
        // not run `rask db update` yet still starts.
        services.AddHostedService<PushModelCheck<TContext>>();
        return services;
    }
}

internal sealed class PushModelCheck<TContext>(IDbContextFactory<TContext> contextFactory)
    : BatteryModelCheck<TContext>(contextFactory)
    where TContext : DbContext
{
    protected override string Battery => "Web Push";

    protected override Type Entity => typeof(PushSubscriber);

    protected override string MapCall => "AddRaskWebPush";
}
