using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Rask.Generators.Shared;

/// <summary>
/// Shared implementation behind the <c>Rask.Jobs</c> and <c>Rask.Outbox</c> registry generators. Both
/// discover every type implementing a marker interface and emit a per-assembly <c>[ModuleInitializer]</c>
/// that maps a stored name to its CLR type, so a queued job or outbox message rehydrates with no runtime
/// <c>Type.GetType</c> or reflection.
/// </summary>
/// <remarks>
/// The two generators were byte-identical copies and drifted into the same bug together; they share this
/// base so a fix to the naming rules (see <c>SymbolRegistration</c>) can only ever land in both. Public
/// because Roslyn only discovers a <c>[Generator]</c> on a public type, and a public type cannot derive
/// from an internal base.
/// </remarks>
public abstract class RegistryGeneratorBase : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor Rask035 = new(
        "RASK035",
        "Job or outbox event type cannot be registered",
        "{0} type '{1}' {2}; it is skipped, so it will fail to deserialize and dead-letter at runtime — {3}",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        description: "A Warning rather than an Error because the rest of the assembly still builds — but the type is "
                     + "left out of the generated registry, so enqueuing it writes a row the processor cannot rehydrate: "
                     + "it retries until MaxAttempts and then dead-letters. Before this existed the type was skipped "
                     + "silently, which is what made the failure hard to place.",
        helpLinkUri: DiagnosticHelp.Link("RASK035"));

    /// <summary>Fully-qualified marker interface a type must implement, e.g. <c>Rask.Jobs.IJob</c>.</summary>
    protected abstract string MarkerInterface { get; }

    /// <summary>Namespace the generated registry class lives in.</summary>
    protected abstract string GeneratedNamespace { get; }

    /// <summary>Name of the generated registry class.</summary>
    protected abstract string RegistryClassName { get; }

    /// <summary>
    /// Fully-qualified group-replacing registration method, e.g.
    /// <c>global::Rask.Jobs.JobSerializerRegistry.Replace</c>. It takes this assembly's generated registry
    /// class as the group key and the complete set of entries it owns.
    /// </summary>
    protected abstract string ReplaceMethod { get; }

    /// <summary>Hint name of the generated file.</summary>
    protected abstract string HintName { get; }

    /// <summary>How RASK035 names the artifact, e.g. <c>Background job</c> or <c>Outbox event</c>.</summary>
    protected abstract string ArtifactNoun { get; }

    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Read the abstract members into locals so the pipeline lambdas capture plain strings rather than
        // `this` — keeps the incremental cache keyed on values, not on a generator instance.
        var marker = MarkerInterface;
        var noun = ArtifactNoun;
        var generatedNamespace = GeneratedNamespace;
        var registryClass = RegistryClassName;
        var replaceMethod = ReplaceMethod;
        var hintName = HintName;

        var candidates = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is TypeDeclarationSyntax t && t.BaseList is { Types.Count: > 0 },
                (ctx, _) => GetCandidate(ctx, marker, noun))
            .Where(candidate => candidate is not null)
            .Select((candidate, _) => candidate!);

        context.RegisterSourceOutput(
            candidates.Collect(),
            (spc, all) => Emit(spc, all, generatedNamespace, registryClass, replaceMethod, hintName));
    }

    private static Candidate? GetCandidate(GeneratorSyntaxContext ctx, string markerInterface, string noun)
    {
        if (ctx.Node is not TypeDeclarationSyntax typeDecl ||
            ctx.SemanticModel.GetDeclaredSymbol(typeDecl) is not INamedTypeSymbol symbol)
        {
            return null;
        }

        if (!SymbolRegistration.ImplementsMarker(symbol, markerInterface))
        {
            return null;
        }

        // An abstract base carrying the marker is a normal way to model a hierarchy, not a mistake —
        // skip it silently rather than warning on every such declaration.
        if (symbol.IsAbstract)
        {
            return null;
        }

        if (SymbolRegistration.DescribeUnregisterableWithRemedy(symbol) is { } unregisterable)
        {
            return new Candidate(
                Key: null,
                TypeExpression: null,
                Problem: unregisterable.Problem,
                Remedy: unregisterable.Remedy,
                DisplayName: SymbolRegistration.RuntimeName(symbol),
                Noun: noun,
                Location: SymbolLocation.From(symbol));
        }

        return new Candidate(
            Key: SymbolRegistration.RuntimeName(symbol),
            TypeExpression: SymbolRegistration.TypeExpression(symbol),
            Problem: null,
            Remedy: null,
            DisplayName: SymbolRegistration.RuntimeName(symbol),
            Noun: noun,
            Location: null);
    }

    private static void Emit(
        SourceProductionContext context,
        ImmutableArray<Candidate> candidates,
        string generatedNamespace,
        string registryClass,
        string replaceMethod,
        string hintName)
    {
        // Warn once per unreachable type. A type split across partial declarations that each carry the
        // marker is visited once per partial, so dedup by name before reporting.
        var warned = new HashSet<string>();
        foreach (var candidate in candidates)
        {
            if (candidate.Problem is not null && warned.Add(candidate.DisplayName))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Rask035,
                    candidate.Location?.ToLocation(),
                    candidate.Noun,
                    candidate.DisplayName,
                    candidate.Problem,
                    candidate.Remedy));
            }
        }

        var entries = candidates
            .Where(c => c.Key is not null)
            .Select(c => (Key: c.Key!, TypeExpression: c.TypeExpression!))
            .Distinct()
            .OrderBy(e => e.Key, System.StringComparer.Ordinal)
            .ToArray();

        if (entries.Length == 0)
        {
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine($"namespace {generatedNamespace}");
        sb.AppendLine("{");
        sb.AppendLine($"    internal static class {registryClass}");
        sb.AppendLine("    {");
        // Init() only bootstraps; RefreshAll() holds the registrations so the hot-reload
        // coordinator can re-invoke them after a metadata update ([ModuleInitializer] never runs
        // twice). RaskHotReload.RefreshTargetTypeNames lists both emitted classes by name.
        //
        // One Replace call keyed on this class, not a run of upserts: an upsert makes a rename
        // *additive*, so renaming a job or event under `rask dev` would leave the old name resolving
        // to a type no longer produced until the process restarted. Replace swaps this assembly's whole
        // contribution in a single store and leaves every other contributor alone.
        sb.AppendLine("        [global::System.Runtime.CompilerServices.ModuleInitializer]");
        sb.AppendLine("        internal static void Init() => RefreshAll();");
        sb.AppendLine();
        sb.AppendLine("        internal static void RefreshAll()");
        sb.AppendLine("        {");
        sb.Append("            ")
          .Append(replaceMethod)
          .AppendLine("(typeof(" + registryClass + "), new (string, global::System.Type)[]");
        sb.AppendLine("            {");
        foreach (var (key, typeExpression) in entries)
        {
            // The key is the runtime Type.FullName; the expression is escaped, fully-qualified C#.
            // They are deliberately different strings — see SymbolRegistration.
            sb.Append("                (")
              .Append(SymbolDisplay.FormatLiteral(key, quote: true))
              .Append(", typeof(")
              .Append(typeExpression)
              .AppendLine(")),");
        }

        sb.AppendLine("            });");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        context.AddSource(hintName, SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private sealed record Candidate(
        string? Key,
        string? TypeExpression,
        string? Problem,
        string? Remedy,
        string DisplayName,
        string Noun,
        SymbolLocation? Location);
}
