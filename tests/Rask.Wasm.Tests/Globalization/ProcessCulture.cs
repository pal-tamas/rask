using System.Globalization;

namespace Rask.Wasm.Tests.Globalization;

/// <summary>
///     Puts the process in a locale the way dotnet-wasm does with the browser's — current AND default
///     thread culture — and puts all four back on dispose.
/// </summary>
/// <remarks>Process-wide, so only for classes in a collection that disables parallelization.</remarks>
internal sealed class ProcessCulture : IDisposable
{
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;
    private readonly CultureInfo? _defaultCulture = CultureInfo.DefaultThreadCurrentCulture;
    private readonly CultureInfo? _defaultUICulture = CultureInfo.DefaultThreadCurrentUICulture;

    private ProcessCulture(string name)
    {
        var culture = CultureInfo.GetCultureInfo(name);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    public static ProcessCulture Set(string name) => new(name);

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _uiCulture;
        CultureInfo.DefaultThreadCurrentCulture = _defaultCulture;
        CultureInfo.DefaultThreadCurrentUICulture = _defaultUICulture;
    }
}
