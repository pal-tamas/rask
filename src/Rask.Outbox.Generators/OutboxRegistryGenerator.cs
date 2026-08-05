using Microsoft.CodeAnalysis;
using Rask.Generators.Shared;

namespace Rask.Outbox.Generators;

/// <summary>
/// Discovers every <c>Rask.Outbox.IOutboxEvent</c> type in the compilation and emits a per-assembly
/// <c>[ModuleInitializer]</c> that registers each with <c>OutboxSerializerRegistry</c> (name → CLR type),
/// so the outbox processor rehydrates a stored message with no runtime <c>Type.GetType</c> / reflection.
/// </summary>
[Generator]
public sealed class OutboxRegistryGenerator : RegistryGeneratorBase
{
    /// <inheritdoc/>
    protected override string MarkerInterface => "Rask.Outbox.IOutboxEvent";

    /// <inheritdoc/>
    protected override string GeneratedNamespace => "Rask.Outbox.Generated";

    /// <inheritdoc/>
    protected override string RegistryClassName => "__RaskOutboxRegistry";

    /// <inheritdoc/>
    protected override string ReplaceMethod => "global::Rask.Outbox.OutboxSerializerRegistry.Replace";

    /// <inheritdoc/>
    protected override string HintName => "__RaskOutboxRegistry.g.cs";

    /// <inheritdoc/>
    protected override string ArtifactNoun => "Outbox event";
}
