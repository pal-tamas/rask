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

    /// <summary>
    /// The DbContext the slice attaches to, instead of writing one: the project's existing context (the
    /// command scans for it) or the one named by <c>--context</c>. <c>null</c> only when the project has no
    /// context at all, which is the one case a run writes <see cref="FeatureGenerator.SharedContextName"/>.
    /// </summary>
    public string? ExistingContext { get; init; }

    /// <summary>
    /// The namespace the <see cref="ExistingContext"/> class lives in, resolved by scanning the project. Lets
    /// the generated slice emit the cross-namespace <c>using</c> it needs to see the context. <c>null</c> when
    /// there's no override, or the context couldn't be located.
    /// </summary>
    public string? ContextNamespace { get; init; }

    /// <summary>
    /// Whether <see cref="ExistingContext"/> can be built as <c>new Ctx(DbContextOptions&lt;Ctx&gt;)</c> — the
    /// shape every scaffolded context has, and the one the <c>--tests</c> persistence test constructs. The
    /// command reads it off the context's source; <c>false</c> when it couldn't, so an unusual context loses
    /// the persistence test rather than gaining one that doesn't compile. Ignored when a context is written.
    /// </summary>
    public bool ContextTakesOptions { get; init; }

    /// <summary>
    /// Whether <see cref="ExistingContext"/> already calls <c>ApplyConfigurationsFromAssembly</c> +
    /// <c>ApplyRaskConventions</c> in <c>OnModelCreating</c> — what the generated entity configurations need
    /// to be picked up. Read off its source; when it's true the next steps drop the "check your context"
    /// note, which would otherwise print on every run against a scaffolded app that already does both.
    /// </summary>
    public bool ContextAppliesRaskConventions { get; init; }

    public string? OutputOverride { get; init; }

    /// <summary>
    /// Both flags raise domain events on the entity; <c>--outbox</c> additionally routes them through the
    /// durable outbox (the events then implement <c>IOutboxEvent</c>). Either turns the machinery on.
    /// </summary>
    public bool UseDomainEvents => UseEvents || UseOutbox;

    /// <summary>Value objects are the default; the other two styles leave the entity's properties primitive.</summary>
    public bool UseValueObjects => Validation == "valueobjects";
}
