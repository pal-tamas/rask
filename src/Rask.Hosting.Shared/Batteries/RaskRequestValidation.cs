using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rask;
using Rask.Core.Forms;
using Rask.Cqrs;

namespace Rask;

/// <summary>
///     Registers the built-in request validators.
/// </summary>
internal static class RaskRequestValidation
{
    /// <summary>
    ///     Adds the DataAnnotations and FluentValidation request validators, so every dispatched
    ///     request is checked before its handler runs. Called for you by the <c>Rask</c> package.
    /// </summary>
    /// <param name="services">The app's services.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddRaskRequestValidation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Open generics: one registration covers every request type, resolved closed by the generated
        // invoker. TryAddEnumerable keeps a second AddRask() call from doubling the pass.
        services.TryAddEnumerable(ServiceDescriptor.Transient(
            typeof(IRequestValidator<>), typeof(DataAnnotationsRequestValidator<>)));
        services.TryAddEnumerable(ServiceDescriptor.Transient(
            typeof(IRequestValidator<>), typeof(FluentValidationRequestValidator<>)));

        // The client-side pre-check. AddRaskCqrsClient asks for this optionally, so registering it is
        // what turns "fail fast in the browser" on; without it a remote request is only validated once
        // it reaches the server.
        services.TryAddSingleton<IRemoteRequestValidator, RaskRemoteRequestValidator>();

        return services;
    }
}
