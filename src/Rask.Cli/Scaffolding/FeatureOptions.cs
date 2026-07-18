namespace Rask.Cli.Scaffolding;

/// <summary>
/// The knobs a <c>generate feature</c> run carries beyond its <see cref="FeatureSpec"/>. One set governs the
/// whole command, so every entity in a run shares a key type, a validation style, and a UI flavour — which is
/// what makes a foreign key's type match the primary key it points at by construction rather than by check.
/// </summary>
internal sealed record FeatureOptions
{
    /// <summary>The primary-key type for every entity in the run: <c>Guid</c> (default), <c>int</c>, or <c>long</c>.</summary>
    public required string IdType { get; init; }

    /// <summary><c>valueobjects</c> (default), <c>dataannotations</c>, or <c>fluent</c>.</summary>
    public required string Validation { get; init; }

    public bool UseBs { get; init; }

    public bool UseModal { get; init; }

    public bool UseSoftDelete { get; init; }

    public bool UseConcurrency { get; init; }

    public bool UseEvents { get; init; }

    public bool UseOutbox { get; init; }

    public bool UseTests { get; init; }

    /// <summary>An existing DbContext to attach to, instead of generating one.</summary>
    public string? ContextOverride { get; init; }

    /// <summary>
    /// The namespace the <see cref="ContextOverride"/> class lives in, resolved by scanning the project. Lets
    /// the generated slice emit the cross-namespace <c>using</c> it needs to see the context. <c>null</c> when
    /// there's no override, or the context couldn't be located.
    /// </summary>
    public string? ContextNamespace { get; init; }

    public string? OutputOverride { get; init; }

    /// <summary>
    /// Both flags raise domain events on the entity; <c>--outbox</c> additionally routes them through the
    /// durable outbox (the events then implement <c>IOutboxEvent</c>). Either turns the machinery on.
    /// </summary>
    public bool UseDomainEvents => UseEvents || UseOutbox;

    /// <summary>Value objects are the default; the other two styles leave the entity's properties primitive.</summary>
    public bool UseValueObjects => Validation == "valueobjects";
}
