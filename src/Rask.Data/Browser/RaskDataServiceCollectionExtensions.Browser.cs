using Microsoft.Extensions.DependencyInjection;

namespace Rask.Data;

public static partial class RaskDataServiceCollectionExtensions
{
    static partial void AddBrowserWiring(IServiceCollection services) => BrowserData.Add(services);
}
