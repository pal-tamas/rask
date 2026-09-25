using Rask.Generators.Shared;

namespace Rask.Api.Generators;

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
