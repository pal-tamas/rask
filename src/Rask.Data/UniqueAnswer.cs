namespace Rask.Data;

/// <summary>What a unique rule last answered, and for which values.</summary>
internal sealed record UniqueAnswer(object?[] Values, bool Taken);
