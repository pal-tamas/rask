using System.Text.Json;

namespace Rask.Cqrs;

/// <summary>Reads a message's result from JSON.</summary>
/// <param name="reader">A reader positioned at the result value.</param>
/// <returns>The decoded result.</returns>
public delegate object? RemoteResultReader(ref Utf8JsonReader reader);
