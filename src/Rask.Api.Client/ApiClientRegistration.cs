namespace Rask.Api.Client;

/// <summary>
///     One generated API client: the type an app injects, and how to build it.
/// </summary>
/// <param name="ClientType">The client's CLR type.</param>
/// <param name="Factory">Builds the client over a configured <see cref="HttpClient" />.</param>
public readonly record struct ApiClientRegistration(Type ClientType, Func<HttpClient, ApiClientOptions, object> Factory);
