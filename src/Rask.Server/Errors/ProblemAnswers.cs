using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Rask.Server.Errors;

// Errors by default: the standard ASP.NET pieces, registered by AddRask and put in the pipeline by MapRask (or
// RaskApp, with its error page), so a hand-wired host answers a request that is not a page with a problem
// document without one call of its own.
internal static class ProblemAnswers
{
    private const string Wired = "Rask.ProblemAnswers";

    /// <summary>ASP.NET's problem-details writer and Rask's exception handler, beside whatever the app registered.</summary>
    // The app's own IExceptionHandler, registered before AddRask, is asked first; ASP.NET asks them in order.
    internal static void Add(IServiceCollection services)
    {
        services.AddProblemDetails();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IExceptionHandler, ProblemAnswer>());
    }

    /// <summary>
    ///     Puts the exception handler in <paramref name="app" />'s pipeline outside Development, once, with the
    ///     status table as its selector — re-executing <paramref name="errorPath" /> for a browser's navigation when
    ///     there is one.
    /// </summary>
    // In Development the developer exception page, which WebApplication puts in front, shows the exception.
    internal static void Use(WebApplication app, string? errorPath = null)
    {
        if (app.Environment.IsDevelopment() || !((IApplicationBuilder)app).Properties.TryAdd(Wired, true))
        {
            return;
        }

        var statuses = app.Services.GetService<RaskServerOptions>()?.Errors ?? new ErrorStatuses();
        var options = new ExceptionHandlerOptions { StatusCodeSelector = statuses.Of };
        if (errorPath is not null)
        {
            options.ExceptionHandlingPath = new PathString(errorPath);
        }

        app.UseExceptionHandler(options);
    }
}
