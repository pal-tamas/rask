namespace Rask.Core.Globalization;

/// <summary>
///     Remembers nothing. The default where there is nowhere to write, and what an app gets when it
///     turns <see cref="RaskCultureOptions.UseCookie" /> off.
/// </summary>
public sealed class NullCulturePersistence : IRaskCulturePersistence
{
    /// <inheritdoc />
    public Task SaveAsync(string culture, string uiCulture, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
