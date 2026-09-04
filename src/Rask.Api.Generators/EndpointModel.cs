using System.Collections.Generic;
using Rask.Generators.Shared;

namespace Rask.Api.Generators;

/// <summary>Where a parameter's value travels.</summary>
internal enum ApiBinding
{
    /// <summary>Substituted into the route template.</summary>
    Route,

    /// <summary>Appended to the query string.</summary>
    Query,

    /// <summary>Sent as the JSON request body.</summary>
    Body,

    /// <summary>Sent as a request header.</summary>
    Header,
}

/// <summary>One parameter of a generated client method.</summary>
/// <param name="Name">The C# parameter name, kept from the action so a named argument still reads.</param>
/// <param name="WireName">The route token, query key or header name it travels under.</param>
/// <param name="Type">The parameter's wire shape.</param>
/// <param name="Binding">Where the value goes.</param>
/// <param name="Fqn">The fully qualified type name to write in the client's signature.</param>
/// <param name="Optional">Whether the action gave it a default, so the client can too.</param>
/// <param name="Default">
///     The action's own default, rendered as a C# literal. Not <c>default</c>: an <c>int page = 1</c>
///     emitted as <c>= default</c> makes the client send <c>page=0</c> whenever the caller omits it,
///     silently overriding the server's default with a zero that type-checks everywhere.
/// </param>
internal sealed record ApiParameter(
    string Name,
    string WireName,
    WireType Type,
    ApiBinding Binding,
    string Fqn,
    bool Optional,
    string Default);

/// <summary>One endpoint, reduced to what a client needs to call it.</summary>
/// <param name="Method">The HTTP method, upper case.</param>
/// <param name="Route">
///     The route template with its constraints stripped — <c>/api/posts/{id}</c>, never
///     <c>/api/posts/{id:int}</c>, because the client substitutes values rather than matching them.
/// </param>
/// <param name="ClientName">The generated client class, e.g. <c>PostsClient</c>.</param>
/// <param name="ClientNamespace">The namespace that class is emitted into.</param>
/// <param name="MethodName">The generated method name.</param>
/// <param name="Parameters">The parameters, in the action's own order.</param>
/// <param name="ResultType">The response's wire shape, or null when the endpoint answers nothing.</param>
/// <param name="ResultFqn">The response type to write in the signature, or null.</param>
/// <param name="DeclaredBy">The action's display name, used in diagnostics.</param>
internal sealed record ApiEndpoint(
    string Method,
    string Route,
    string ClientName,
    string ClientNamespace,
    string MethodName,
    IReadOnlyList<ApiParameter> Parameters,
    WireType? ResultType,
    string? ResultFqn,
    string DeclaredBy);
