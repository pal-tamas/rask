using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rask.Core.Live;

namespace Rask.Data;

/// <summary>
///     What a Rask server host wires for Rask.Data, done by <c>AddRaskData</c> itself in a WebAssembly app: a save
///     refreshes the page's queries about what it wrote, with no app code.
/// </summary>
/// <remarks>
///     Here rather than in the <c>Rask</c> package's browser half, which the trimmer keeps whole: a reference to
///     Rask.Data from there would put EF Core in every WASM bundle, including apps with no database.
/// </remarks>
internal static class BrowserData
{
    internal static void Add(IServiceCollection services)
    {
        services.TryAddSingleton<ISessionWorkScope, BrowserDataScope>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDataChanges, QueryDataChanges>());

        // A refused id says it in the reader's language, through the strings catalog the browser build references.
        HeldRowReference.Sentence = static () =>
            Rask.Core.Globalization.RaskStrings.Get(Rask.Core.Globalization.RaskString.ValidationNotFound, HeldRowReference.Message);

        // The tenant the resolver named is the page's answer, asked again at each event, not a new question per event.
        EventScopeCatalog.KeepForSession(services, typeof(ResolvedTenant));
    }
}
