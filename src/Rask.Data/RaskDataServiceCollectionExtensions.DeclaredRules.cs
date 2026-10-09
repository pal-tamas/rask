using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Data;

/// <content>The check a host adds when the schema is not Rask's.</content>
public static partial class RaskDataServiceCollectionExtensions
{
    /// <summary>
    ///     Has every save run the model's declared unique rules as queries first
    ///     (<see cref="DeclaredRuleCheckInterceptor" />) — for a database Rask did not create, where a declared
    ///     index may not exist.
    /// </summary>
    /// <remarks>
    ///     Placed just before the interceptor that translates a real index's violation: after auditing, which
    ///     stamps the tenant a rule may name, and ahead of the one interceptor that ends a failed save by
    ///     throwing — so this one is always told the save is over and can close the transaction it opened.
    /// </remarks>
    internal static IServiceCollection AddDeclaredRuleChecks(this IServiceCollection services)
    {
        services.AddRaskData();

        if (services.Any(static d => d.ImplementationType == typeof(DeclaredRuleCheckInterceptor)))
        {
            return services;
        }

        var check = ServiceDescriptor.Singleton<ISaveChangesInterceptor, DeclaredRuleCheckInterceptor>();
        var translator = services
            .Select(static (descriptor, at) => (descriptor, at))
            .FirstOrDefault(static d => d.descriptor.ImplementationType == typeof(UniqueViolationInterceptor));

        if (translator.descriptor is null)
        {
            services.Add(check);
        }
        else
        {
            services.Insert(translator.at, check);
        }

        return services;
    }
}
