namespace Rask.Core.Globalization;

/// <summary>
///     Where an explicit culture choice is stored between visits. Implemented per host: a cookie on the
///     web, nothing at all where there is nowhere to write.
/// </summary>
public interface IRaskCulturePersistence
{
    /// <summary>Stores the chosen culture.</summary>
    Task SaveAsync(string culture, string uiCulture, CancellationToken cancellationToken = default);
}
