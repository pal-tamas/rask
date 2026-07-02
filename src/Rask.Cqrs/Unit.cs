namespace Rask.Cqrs;

/// <summary>
/// The result type of a void <see cref="ICommand"/>. Void commands flow through the pipeline as
/// <c>IPipelineBehavior&lt;TCommand, Unit&gt;</c> so a single behavior shape covers queries,
/// result-commands and void-commands alike.
/// </summary>
public readonly struct Unit : IEquatable<Unit>
{
    /// <summary>The single <see cref="Unit"/> value.</summary>
    public static readonly Unit Value;

    /// <summary>A completed task carrying the <see cref="Unit"/> value.</summary>
    public static Task<Unit> Task { get; } = System.Threading.Tasks.Task.FromResult(Value);

    /// <inheritdoc/>
    public bool Equals(Unit other) => true;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Unit;

    /// <inheritdoc/>
    public override int GetHashCode() => 0;

    /// <inheritdoc/>
    public override string ToString() => "()";

    /// <summary>All <see cref="Unit"/> values are equal.</summary>
    public static bool operator ==(Unit left, Unit right) => true;

    /// <summary>All <see cref="Unit"/> values are equal.</summary>
    public static bool operator !=(Unit left, Unit right) => false;
}
