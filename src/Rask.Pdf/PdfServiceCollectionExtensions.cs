using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rask.Hosting.Shared;

namespace Rask.Printing;

/// <summary>Turns PDF printing on for a server app.</summary>
public static class PdfServiceCollectionExtensions
{
    /// <summary>
    /// Registers the app's printer: one headless Chromium, started at the first <see cref="Pdf.From(Rask.Core.Routing.RouteUrl, PdfOptions?)"/>
    /// and closed with the host, configured from <c>Rask:Pdf</c> (<c>ChromiumPath</c>, <c>NoSandbox</c>,
    /// <c>MaxConcurrentPrints</c>, <c>Timeout</c>, <c>MaxPages</c>). Rarely needed: <c>AddRask()</c> — and so every
    /// <c>RaskApp</c> — calls it whenever the app references Rask.Pdf. Idempotent, so an explicit call is harmless.
    /// </summary>
    public static IServiceCollection AddRaskPdf(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.AddRaskOptions<ChromiumSettings>(
                "Rask:Pdf", static (section, o) => section.Bind(o), configure: null, static o => o.Validate()))
        {
            // A singleton the container disposes when the host stops, which closes the browser.
            services.TryAddSingleton(static sp => new ChromiumPrinter(sp.GetRequiredService<ChromiumSettings>()));

            // So Pdf.From in a minimal-API endpoint finds its request, and the request's user.
            services.AddHttpContextAccessor();
        }

        return services;
    }
}
