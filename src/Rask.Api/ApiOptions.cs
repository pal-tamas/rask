namespace Rask.Api;

/// <summary>
///     Configures how a Rask app hosts its HTTP endpoints.
/// </summary>
public sealed class ApiOptions
{
    private string _prefix = "/api";

    /// <summary>
    ///     The path every endpoint of this app's API sits under. Defaults to <c>/api</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This is what makes a wrong URL answerable. Rask ends the pipeline with a catch-all that
    ///         serves the app for anything unmatched — right for a page, since the app's own router shows
    ///         its not-found page — but wrong under an API path, where the caller wanted JSON and gets a
    ///         200 and a web page instead. Naming the prefix lets <see cref="RaskApiEndpointExtensions.MapRaskApi" />
    ///         answer 404 there and leave every other path alone.
    ///     </para>
    ///     <para>
    ///         An app whose endpoints are spread over several prefixes should set this to the narrowest
    ///         path covering them, or turn the guard off with <see cref="NotFound" /> and answer for
    ///         itself.
    ///     </para>
    /// </remarks>
    /// <exception cref="ArgumentException">The value is empty, or does not start with <c>/</c>.</exception>
    public string Prefix
    {
        get => _prefix;
        set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);

            if (!value.StartsWith('/'))
            {
                throw new ArgumentException(
                    $"The API prefix must start with '/', but was '{value}'.", nameof(value));
            }

            _prefix = value.Length > 1 ? value.TrimEnd('/') : value;
        }
    }

    /// <summary>
    ///     Whether an unmatched request under <see cref="Prefix" /> answers 404 with a problem document
    ///     rather than falling through to the app. On by default.
    /// </summary>
    public bool NotFound { get; set; } = true;

    /// <summary>
    ///     Whether <c>MapRaskApi</c> maps MVC controllers. On by default; turn it off in an app whose
    ///     endpoints are all minimal APIs, so no controller is discovered and MVC never runs.
    /// </summary>
    public bool Controllers { get; set; } = true;
}
