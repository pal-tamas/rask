using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators.Analyzers;

/// <summary>
///     RASK165 — a configuration call the generators read from the source — <c>KeepEmpty(…)</c>, <c>Columns(…)</c>,
///     <c>IsUnique("…")</c> — written where they cannot see it; RASK172 — the same for an operation's roles
///     (<c>Operation(x =&gt; x.M, roles: …)</c>), which would leave its generated entry open.
/// </summary>
/// <remarks>
///     The generated model, writes and read face are generated before any EF model is built, so they read these calls
///     from the entity's own static <c>Configure</c>, as written there: a member named by a lambda, a pattern that is a
///     constant. Made in a helper, another class, <c>OnModelCreating</c> or an <c>IEntityTypeConfiguration</c>, the call
///     still configures the column, but the generated code behaves as if it were never made — an empty text is
///     cleared, the read face keeps the default names, a form never asks the store.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnreadConfigurationAnalyzer : DiagnosticAnalyzer
{
    private const string Configure = "Configure";

    private static readonly DiagnosticDescriptor Rask165 = new(
        "RASK165",
        "A configuration call the generators read is written where they cannot see it",
        "'{0}' configures '{1}', but the generated code never sees it: {2} — write it directly in the static Configure of '{1}'",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "KeepEmpty, Columns and IsUnique(message) are read from the entity's own static Configure, as "
                     + "written there, because the generated model, writes and read face exist before any EF model "
                     + "is built. Anywhere else the column is still configured, but the generated code acts as if "
                     + "the call were never made.",
        helpLinkUri: DiagnosticHelp.Link("RASK165"));

    // The roles of an operation the entry's generator cannot read would leave the entry open to everyone: an error, not
    // RASK165's warning.
    private static readonly DiagnosticDescriptor Rask172 = new(
        "RASK172",
        "The roles of an operation are written where its generated entry cannot read them",
        "'Operation' names roles for '{0}', but its generated entry never sees them, so it would run for everyone: {1} — "
        + "write it directly in the static Configure of '{0}', the operation as a lambda and the roles as a constant",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "b.Operation(x => x.M, roles: …) is read from the aggregate's own static Configure where the entry "
                     + "Supply.M(id, version, …) is generated, as [Authorize(Roles = …)] is read by the compiler. Made "
                     + "anywhere else, or with roles that are no constant, it would leave the entry asking nothing, so the "
                     + "build stops instead; roles that are no constant leave no entry at all.",
        helpLinkUri: DiagnosticHelp.Link("RASK172"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rask165, Rask172);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationAction(Analyze, OperationKind.Invocation);
    }

    private static void Analyze(OperationAnalysisContext context)
    {
        var call = (IInvocationOperation)context.Operation;
        var method = call.TargetMethod.ReducedFrom ?? call.TargetMethod;
        if (Read(method) is not { } read ||
            method.Parameters.Length == 0 ||
            method.Parameters[0].Type is not INamedTypeSymbol { TypeArguments.Length: 1 } builder ||
            builder.TypeArguments[0] is not INamedTypeSymbol entity ||
            !(read is ReadCall.IsUnique or ReadCall.Operation ? AggregateShape.IsAggregate(entity) : AggregateShape.IsEntity(entity)) ||
            call.Syntax is not InvocationExpressionSyntax syntax ||
            Unseen(syntax, method, read, entity, context) is not { } why)
        {
            return;
        }

        context.ReportDiagnostic(read == ReadCall.Operation
            ? Diagnostic.Create(Rask172, syntax.GetLocation(), entity.Name, why)
            : Diagnostic.Create(Rask165, syntax.GetLocation(), method.Name, entity.Name, why));
    }

    private enum ReadCall
    {
        KeepEmpty,
        Columns,
        IsUnique,
        Operation,
    }

    // The calls a generator reads, by the type that declares them.
    private static ReadCall? Read(IMethodSymbol method) =>
        (method.ContainingType.ToDisplayString(), method.Name, method.Parameters.Length) switch
        {
            ("Rask.Data.KeepEmptyBuilderExtensions", "KeepEmpty", _) => ReadCall.KeepEmpty,
            ("Rask.Data.ValueObjectColumnsBuilderExtensions", "Columns", _) => ReadCall.Columns,
            ("Rask.Data.ViolationMessageBuilderExtensions", "IsUnique", 2) => ReadCall.IsUnique,
            ("Rask.Data.OperationBuilderExtensions", "Operation", _) => ReadCall.Operation,
            _ => null,
        };

    // Why the generator does not see the call, or null when it does.
    private static string? Unseen(
        InvocationExpressionSyntax call, IMethodSymbol method, ReadCall read, INamedTypeSymbol entity, OperationAnalysisContext context)
    {
        // `b.KeepEmpty(…)` passes one argument fewer than the extension declares; `KeepEmptyBuilderExtensions.KeepEmpty(b, …)` does not.
        if (call.Expression is not MemberAccessExpressionSyntax || call.ArgumentList.Arguments.Count == method.Parameters.Length)
        {
            return "it is called as a static method";
        }

        var declaration = call.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        var model = context.Operation.SemanticModel;
        if (declaration is null || model?.GetDeclaredSymbol(declaration, context.CancellationToken) is not IMethodSymbol holder ||
            !holder.IsStatic || !string.Equals(holder.Name, Configure, System.StringComparison.Ordinal) ||
            !SymbolEqualityComparer.Default.Equals(holder.ContainingType.OriginalDefinition, entity.OriginalDefinition))
        {
            return "it is made outside that method";
        }

        if (read == ReadCall.IsUnique)
        {
            return null;
        }

        var arguments = call.ArgumentList.Arguments;
        if (read == ReadCall.Operation)
        {
            if (arguments.Count == 0 || OperationGenerator.NamedOperation(arguments[0].Expression, model, context.CancellationToken) is null)
            {
                return "its operation is neither a lambda naming a method nor a constant name";
            }

            return RolesOf(arguments) is { } named &&
                   model.GetConstantValue(named.Expression, context.CancellationToken) is not { HasValue: true, Value: string or null }
                ? "its roles are not a constant"
                : null;
        }

        if (arguments.Count == 0 ||
            arguments[0].Expression is not LambdaExpressionSyntax { Body: MemberAccessExpressionSyntax })
        {
            return "its member is not a lambda naming a property";
        }

        return read == ReadCall.Columns &&
               (arguments.Count < 2 || model.GetConstantValue(arguments[1].Expression, context.CancellationToken) is not { HasValue: true, Value: string })
            ? "its pattern is not a constant"
            : null;
    }

    // The roles argument: named, or the second one given in place.
    private static ArgumentSyntax? RolesOf(SeparatedSyntaxList<ArgumentSyntax> arguments) =>
        arguments.Skip(1).FirstOrDefault(static a => string.Equals(a.NameColon?.Name.Identifier.ValueText, "roles", System.StringComparison.Ordinal))
        ?? (arguments.Count > 1 && arguments[1].NameColon is null ? arguments[1] : null);
}
