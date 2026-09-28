using Rask.Wire;

namespace Rask.Auth.Client;

/// <summary>How the browser half reaches the app's auth endpoints.</summary>
public sealed class AuthClientOptions
{
    private string _prefix = AuthApi.DefaultPrefix;

    /// <summary>
    /// The path the endpoints sit under. Must match the server's <c>AuthOptions.ApiPrefix</c>.
    /// </summary>
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
                    $"The auth API prefix must start with '/', but was '{value}'.", nameof(value));
            }

            _prefix = value.Length > 1 ? value.TrimEnd('/') : value;
        }
    }
}
