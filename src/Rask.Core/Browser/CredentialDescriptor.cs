using System.Text.Json.Serialization;

namespace Rask.Core.Browser;

/// <summary>A reference to an existing credential (to exclude on register, or allow on authenticate).</summary>
/// <param name="Id">The credential id as base64url.</param>
/// <param name="Transports">Optional transport hints, e.g. <c>["internal", "usb"]</c>.</param>
/// <param name="Type">Credential type — always <c>"public-key"</c>.</param>
public sealed record CredentialDescriptor(
    string Id,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string[]? Transports = null,
    string Type = "public-key");
