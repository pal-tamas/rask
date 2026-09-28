using System.Text.Json;

namespace Rask.Cqrs;

/// <summary>Writes a message's result as JSON.</summary>
/// <param name="writer">The JSON writer.</param>
/// <param name="result">The value the handler returned; may be null for a nullable result type.</param>
public delegate void RemoteResultWriter(Utf8JsonWriter writer, object? result);
