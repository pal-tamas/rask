using Microsoft.CodeAnalysis;
using Rask.Generators.Shared;

namespace Rask.Jobs.Generators;

/// <summary>
/// Discovers every <c>Rask.Jobs.IJob</c> type in the compilation and emits a per-assembly
/// <c>[ModuleInitializer]</c> that registers each with <c>JobSerializerRegistry</c> (name → CLR type), so the
/// job processor rehydrates a stored job with no runtime <c>Type.GetType</c> / reflection.
/// </summary>
[Generator]
public sealed class JobRegistryGenerator : RegistryGeneratorBase
{
    /// <inheritdoc/>
    protected override string MarkerInterface => "Rask.Jobs.IJob";

    /// <inheritdoc/>
    protected override string GeneratedNamespace => "Rask.Jobs.Generated";

    /// <inheritdoc/>
    protected override string RegistryClassName => "__RaskJobsRegistry";

    /// <inheritdoc/>
    protected override string ReplaceMethod => "global::Rask.Jobs.JobSerializerRegistry.Replace";

    /// <inheritdoc/>
    protected override string HintName => "__RaskJobsRegistry.g.cs";

    /// <inheritdoc/>
    protected override string ArtifactNoun => "Background job";
}
