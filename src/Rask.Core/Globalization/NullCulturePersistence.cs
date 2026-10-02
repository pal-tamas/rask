namespace Rask.Core.Globalization;

/// <summary>
///     Remembers nothing. The default on a host that does not reference <c>Rask.Web</c>, where the cookie
///     is written; turning <see cref="RaskCultureOptions.UseCookie" /> off skips persistence on every host.
/// </summary>
public sealed class NullCulturePersistence : IRaskCulturePersistence
{
    /// <inheritdoc />
    public Task SaveAsync(string culture, string uiCulture, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
