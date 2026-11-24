using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators;

/// <summary>
/// Gives every public method of a <c>partial</c> aggregate root — an operation — a static entry of the same name that
/// runs it as one write: <c>await Supply.Change(id, version, details, parts)</c> loads the aggregate at the version the
/// caller read, asks the roles its <c>Configure</c> names, calls <c>Change(details, parts)</c> and saves, in one
/// transaction (<c>Rask.Data.GeneratedOperations</c>).
/// </summary>
/// <remarks>
/// <para>
/// An operation, by convention: a public instance method declared on the aggregate itself, not generic, taking no
/// <c>ref</c>/<c>out</c>/<c>in</c>, that returns <c>void</c>, <c>Task</c>, <c>ValueTask</c> or the aggregate (a
/// fluent <c>Driver Change(…)</c>). A method that returns anything else is a question, not a change, and gets none;
/// so do the conventions (<c>Validate</c>, <c>ValidateDelete</c>, <c>On</c>) and the names the writes already take.
/// </para>
/// <para>
/// A factory, by convention: a static method declared on the aggregate that returns it (or a <c>Task</c> of it), of any
/// accessibility — written non-public, because its generated entry takes the public name:
/// <c>await Supply.Register(company, details, parts)</c> builds it, asks its roles and rules and saves it in one
/// transaction, and hands back its id. A factory left public would hide that entry, which is RASK173.
/// </para>
/// <para>
/// Each operation and factory also gets <c>Supply.MayChange</c>: whether the signed-in principal holds one of its
/// roles (always, for one that names none), for a page to hide what it may not do.
/// </para>
/// <para>
/// The entry is a static overload in the aggregate's own class, which is why only a <c>partial</c> aggregate gets
/// them; called by the type's name it never meets the instance method, and called from inside the aggregate it is
/// RASK170.
/// </para>
/// </remarks>
[Generator]
public sealed class OperationGenerator : IIncrementalGenerator
{
    /// <summary>The tool name every entry's <c>[GeneratedCode]</c> carries, which RASK170 recognises it by.</summary>
    internal const string Tool = "Rask.Operations";

    private const string OperationCall = "Operation";
    private const string RolesParameter = "roles";
    private const string TaskFqn = "global::System.Threading.Tasks.Task";

    private const string FailuresTask =
        "global::System.Threading.Tasks.Task<global::System.Collections.Generic.IReadOnlyList<global::Rask.Wire.FieldFailure>>";

    // The names the writes and the conventions already take: an entry by one of them would hide a generated write.
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "Create", "Find", "Update", "Delete", "Save", "Model", "Validate", "ValidateDelete", "On", "Configure",
    };

    // The entry's own parameters, which an operation's may not shadow; a factory's entry adds only the token.
    private static readonly string[] EntryParameters = ["id", "version", "cancellationToken"];
    private static readonly string[] FactoryEntryParameters = ["cancellationToken"];

    private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    internal static readonly DiagnosticDescriptor Rask171 = new(
        "RASK171",
        "Operation parameter takes a name its generated entry needs",
        "'{0}.{1}' has a parameter named '{2}', which its generated entry '{0}.{1}(id, version, …)' takes for itself, so "
        + "no entry is generated — rename the parameter",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        description: "Every public method of a partial aggregate gets a static entry of the same name that loads the "
                     + "aggregate by 'id' at 'version', runs the method and saves, cancelled by 'cancellationToken'. An "
                     + "operation whose own parameter has one of those names would make the entry ambiguous.",
        helpLinkUri: DiagnosticHelp.Link("RASK171"));

    internal static readonly DiagnosticDescriptor Rask173 = new(
        "RASK173",
        "A factory is public, so its generated entry cannot be called",
        "'{0}.{1}' is a public factory, so a call to '{0}.{1}(…)' reaches it rather than the generated entry that saves "
        + "what it builds — make it private or internal, and '{0}.{1}(…)' builds, checks and saves the {0} in one write",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        description: "A static method of a partial aggregate that returns it is its factory, and gets a public static "
                     + "entry of the same name that builds it, asks its roles and rules, saves it in one transaction and "
                     + "returns its id. The entry takes the same parameters, so a public factory wins every call and the "
                     + "entry is never generated; the factory is written non-public instead.",
        helpLinkUri: DiagnosticHelp.Link("RASK173"));

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var aggregates = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { BaseList.Types.Count: > 0 } declaration &&
                                    declaration.Modifiers.Any(SyntaxKind.PartialKeyword),
                static (ctx, ct) => GetAggregate(ctx, ct))
            .Where(static aggregate => aggregate is not null)
            .Select(static (aggregate, _) => aggregate!);

        context.RegisterSourceOutput(aggregates.Collect(), static (spc, all) => Emit(spc, all));
    }

    private static OperatedAggregate? GetAggregate(GeneratorSyntaxContext ctx, CancellationToken cancellationToken)
    {
        if (ctx.SemanticModel.GetDeclaredSymbol((ClassDeclarationSyntax)ctx.Node, cancellationToken) is not INamedTypeSymbol symbol ||
            symbol.ContainingType is not null ||
            !AggregateShape.IsWritableAggregate(symbol) ||
            !AggregateShape.TryGetIdType(symbol, out var idType) ||
            idType is null or INamedTypeSymbol { IsTupleType: true })
        {
            return null;
        }

        var roles = RolesOf(symbol, ctx.SemanticModel.Compilation, cancellationToken);
        var operations = new List<Operation>();
        var problems = new List<OperationProblem>();

        foreach (var method in symbol.GetMembers().OfType<IMethodSymbol>().Where(m => IsOperation(m, symbol) || IsFactory(m, symbol)))
        {
            var location = SymbolLocation.From(method);
            var factory = method.IsStatic;
            if (factory && method.DeclaredAccessibility == Accessibility.Public)
            {
                problems.Add(new OperationProblem(Rask173.Id, method.Name, "", location));
                continue;
            }

            var taken = factory ? FactoryEntryParameters : EntryParameters;
            if (method.Parameters.FirstOrDefault(p => taken.Contains(p.Name, StringComparer.Ordinal)) is { } clash)
            {
                problems.Add(new OperationProblem(Rask171.Id, method.Name, clash.Name, location));
                continue;
            }

            // Roles that are no constant leave no entry rather than an open one; RASK172 says why.
            roles.TryGetValue(method.Name, out var named);
            if (named is { Constant: false })
            {
                continue;
            }

            operations.Add(new Operation(
                method.Name,
                factory ? FactoryCallOf(method, symbol)! : CallOf(method, symbol)!,
                named?.Roles,
                new EquatableArray<OperationParameter>(method.Parameters.Select(Parameter)),
                factory));
        }

        if (operations.Count == 0 && problems.Count == 0)
        {
            return null;
        }

        return new OperatedAggregate(
            symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            symbol.Name,
            symbol.ContainingNamespace.IsGlobalNamespace ? null : symbol.ContainingNamespace.ToDisplayString(),
            idType.ToDisplayString(TypeFormat),
            AggregateShape.HasVersion(symbol),
            new EquatableArray<Operation>(operations),
            new EquatableArray<OperationProblem>(problems));
    }

    private static bool IsOperation(IMethodSymbol method, INamedTypeSymbol aggregate) =>
        method is { MethodKind: MethodKind.Ordinary, DeclaredAccessibility: Accessibility.Public, IsStatic: false }
        && !method.IsGenericMethod
        && !method.IsOverride
        && !method.IsImplicitlyDeclared
        && !Reserved.Contains(method.Name)
        && method.Parameters.All(static p => p.RefKind == RefKind.None)
        && CallOf(method, aggregate) is not null;

    // A static method that returns the aggregate it is declared on: how it is born.
    private static bool IsFactory(IMethodSymbol method, INamedTypeSymbol aggregate) =>
        method is { MethodKind: MethodKind.Ordinary, IsStatic: true }
        && !method.IsGenericMethod
        && !method.IsImplicitlyDeclared
        && !Reserved.Contains(method.Name)
        && method.Parameters.All(static p => p.RefKind == RefKind.None)
        && !method.GetAttributes().Any(static a => a.AttributeClass is { Name: "GeneratedCodeAttribute" })
        && FactoryCallOf(method, aggregate) is not null;

    // How the factory's entry hands the build to the run, by what it returns; null for a method that builds no aggregate.
    private static string? FactoryCallOf(IMethodSymbol method, INamedTypeSymbol aggregate)
    {
        if (SymbolEqualityComparer.Default.Equals(method.ReturnType, aggregate))
        {
            return TaskFqn + ".FromResult({0})";
        }

        var argument = method.ReturnType is INamedTypeSymbol { TypeArguments.Length: 1 } generic ? generic.TypeArguments[0] : null;
        if (argument is null || !SymbolEqualityComparer.Default.Equals(argument, aggregate))
        {
            return null;
        }

        return method.ReturnType.OriginalDefinition.ToDisplayString() switch
        {
            "System.Threading.Tasks.Task<TResult>" => "{0}",
            "System.Threading.Tasks.ValueTask<TResult>" => "{0}.AsTask()",
            _ => null,
        };
    }

    // How the entry hands the method to the run, by what it returns; null for a method that returns an answer.
    private static string? CallOf(IMethodSymbol method, INamedTypeSymbol aggregate)
    {
        if (method.ReturnsVoid || SymbolEqualityComparer.Default.Equals(method.ReturnType, aggregate))
        {
            return "{ aggregate.{0}; return " + TaskFqn + ".CompletedTask; }";
        }

        var returned = method.ReturnType.OriginalDefinition.ToDisplayString();
        var argument = method.ReturnType is INamedTypeSymbol { TypeArguments.Length: 1 } generic ? generic.TypeArguments[0] : null;
        var ofSelf = argument is not null && SymbolEqualityComparer.Default.Equals(argument, aggregate);

        return returned switch
        {
            "System.Threading.Tasks.Task" => "aggregate.{0}",
            "System.Threading.Tasks.ValueTask" => "aggregate.{0}.AsTask()",
            "System.Threading.Tasks.Task<TResult>" when ofSelf => "aggregate.{0}",
            "System.Threading.Tasks.ValueTask<TResult>" when ofSelf => "aggregate.{0}.AsTask()",
            _ => null,
        };
    }

    private static OperationParameter Parameter(IParameterSymbol parameter) =>
        new(parameter.Type.ToDisplayString(TypeFormat), Identifier(parameter.Name), DefaultOf(parameter));

    private static string Identifier(string name) =>
        SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;

    // A default the method's parameter has, restated so the entry can be called the same way.
    private static string? DefaultOf(IParameterSymbol parameter)
    {
        if (!parameter.HasExplicitDefaultValue)
        {
            return null;
        }

        var value = parameter.ExplicitDefaultValue;
        if (value is null)
        {
            return "default";
        }

        var type = parameter.Type.ToDisplayString(TypeFormat);
        return value switch
        {
            string text => SymbolDisplay.FormatLiteral(text, quote: true),
            char single => SymbolDisplay.FormatLiteral(single, quote: true),
            bool flag => flag ? "true" : "false",
            _ => "(" + type + ")" + SymbolDisplay.FormatPrimitive(value, quoteStrings: true, useHexadecimalNumbers: false),
        };
    }

    // `b.Operation(x => x.M, roles: …)` in the aggregate's own static Configure, read as KeepEmpty and IsUnique are.
    private static Dictionary<string, NamedRoles> RolesOf(
        INamedTypeSymbol symbol, Compilation compilation, CancellationToken cancellationToken)
    {
        var named = new Dictionary<string, NamedRoles>(StringComparer.Ordinal);

        foreach (var reference in symbol.GetMembers("Configure").OfType<IMethodSymbol>()
                     .Where(static m => m.IsStatic)
                     .SelectMany(static m => m.DeclaringSyntaxReferences))
        {
            var syntax = reference.GetSyntax(cancellationToken);
            var model = compilation.GetSemanticModel(syntax.SyntaxTree);

            foreach (var call in syntax.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (call is not
                    {
                        Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: OperationCall },
                        ArgumentList.Arguments: { Count: > 0 } arguments,
                    } ||
                    NamedOperation(arguments[0].Expression, model, cancellationToken) is not { } operation)
                {
                    continue;
                }

                var roles = arguments.Skip(1).FirstOrDefault(static a =>
                    string.Equals(a.NameColon?.Name.Identifier.ValueText, RolesParameter, StringComparison.Ordinal))
                    ?? (arguments.Count > 1 && arguments[1].NameColon is null ? arguments[1] : null);

                if (roles is null)
                {
                    continue;
                }

                var constant = model.GetConstantValue(roles.Expression, cancellationToken);
                named[operation] = new NamedRoles(
                    constant is { HasValue: true, Value: string or null },
                    constant.HasValue ? constant.Value as string : null);
            }
        }

        return named;
    }

    // `x => x.Distribute`, or `nameof(Register)` for a factory, whose method group has no single delegate type.
    internal static string? NamedOperation(ExpressionSyntax argument, SemanticModel model, CancellationToken cancellationToken) =>
        argument switch
        {
            LambdaExpressionSyntax { Body: MemberAccessExpressionSyntax member } => member.Name.Identifier.ValueText,
            _ => model.GetConstantValue(argument, cancellationToken) is { HasValue: true, Value: string name } ? name : null,
        };

    private static void Emit(SourceProductionContext context, ImmutableArray<OperatedAggregate> all)
    {
        foreach (var aggregate in all.Distinct())
        {
            foreach (var problem in aggregate.Problems)
            {
                context.ReportDiagnostic(string.Equals(problem.Id, Rask173.Id, StringComparison.Ordinal)
                    ? Diagnostic.Create(Rask173, problem.Location?.ToLocation(), aggregate.Name, problem.Method)
                    : Diagnostic.Create(Rask171, problem.Location?.ToLocation(), aggregate.Name, problem.Method, problem.Clash));
            }

            if (aggregate.Operations.Count == 0)
            {
                continue;
            }

            var hint = aggregate.FullyQualifiedName.Replace("global::", "") + ".Operations.g.cs";
            context.AddSource(hint, SourceText.From(Render(aggregate), Encoding.UTF8));
        }
    }

    private static string Render(OperatedAggregate aggregate)
    {
        var s = new StringBuilder();
        s.AppendLine("// <auto-generated/>");
        s.AppendLine("#nullable enable");
        if (aggregate.Namespace is { } ns)
        {
            s.Append("namespace ").Append(ns).AppendLine(";");
        }

        s.AppendLine();
        s.Append("partial class ").AppendLine(aggregate.Name);
        s.AppendLine("{");

        foreach (var operation in aggregate.Operations)
        {
            if (operation.Factory)
            {
                AppendFactoryEntry(s, aggregate, operation);
                AppendFactoryDryRun(s, aggregate, operation);
            }
            else
            {
                AppendEntry(s, aggregate, operation);
                AppendDryRun(s, aggregate, operation);
            }
        }

        // One answer per name: overloads of an operation share its roles.
        foreach (var operation in aggregate.Operations.GroupBy(static o => o.Name, StringComparer.Ordinal).Select(static g => g.First()))
        {
            AppendMay(s, operation);
        }

        s.AppendLine("}");
        return s.ToString();
    }

    private static void AppendEntry(StringBuilder s, OperatedAggregate aggregate, Operation operation)
    {
        var arguments = string.Join(", ", operation.Parameters.Select(static p => p.Name));
        var call = operation.Call.Replace("{0}", operation.Name + "(" + arguments + ")");

        s.Append("    /// <summary>Runs <c>").Append(operation.Name)
            .AppendLine("(…)</c> as one write: the aggregate loaded, the method called on it, and saved — its rules asked — in one transaction.</summary>");
        s.AppendLine("    /// <param name=\"id\">The id of the aggregate.</param>");
        if (aggregate.Versioned)
        {
            s.AppendLine("    /// <param name=\"version\">The version the caller read: a save since is refused.</param>");
        }

        AppendParameterDocs(s, operation);
        s.AppendLine("    /// <param name=\"cancellationToken\">Cancels the operation.</param>");
        s.AppendLine("    /// <returns>A task that completes once the operation has committed.</returns>");
        s.Append("    [global::System.CodeDom.Compiler.GeneratedCode(\"").Append(Tool).AppendLine("\", \"1\")]");
        s.Append("    public static ").Append(TaskFqn).Append(' ').Append(operation.Name).Append('(')
            .Append(aggregate.IdType).Append(" id, ");
        if (aggregate.Versioned)
        {
            s.Append("int version, ");
        }

        AppendParameters(s, operation);
        s.AppendLine("global::System.Threading.CancellationToken cancellationToken = default) =>");
        s.Append("        global::Rask.Data.GeneratedOperations.Run<").Append(aggregate.FullyQualifiedName).Append(">(\"")
            .Append(operation.Name).Append("\", ")
            .Append(RolesLiteral(operation))
            .Append(", id!, ").Append(aggregate.Versioned ? "version" : "null").Append(", aggregate => ").Append(call)
            .AppendLine(", cancellationToken);");
        s.AppendLine();
    }

    private static void AppendFactoryEntry(StringBuilder s, OperatedAggregate aggregate, Operation operation)
    {
        var arguments = string.Join(", ", operation.Parameters.Select(static p => p.Name));
        var call = operation.Call.Replace("{0}", operation.Name + "(" + arguments + ")");

        s.Append("    /// <summary>Builds a new <c>").Append(aggregate.Name).Append("</c> with <c>").Append(operation.Name)
            .AppendLine("(…)</c> and saves it — its rules asked — in one transaction.</summary>");
        AppendParameterDocs(s, operation);
        s.AppendLine("    /// <param name=\"cancellationToken\">Cancels the operation.</param>");
        s.AppendLine("    /// <returns>The id of the saved aggregate, once committed.</returns>");
        s.Append("    [global::System.CodeDom.Compiler.GeneratedCode(\"").Append(Tool).AppendLine("\", \"1\")]");
        s.Append("    public static ").Append(TaskFqn).Append('<').Append(aggregate.IdType).Append("> ").Append(operation.Name).Append('(');
        AppendParameters(s, operation);
        s.AppendLine("global::System.Threading.CancellationToken cancellationToken = default) =>");
        s.Append("        global::Rask.Data.GeneratedOperations.Build<").Append(aggregate.FullyQualifiedName).Append(", ")
            .Append(aggregate.IdType).Append(">(\"").Append(operation.Name).Append("\", ").Append(RolesLiteral(operation))
            .Append(", () => ").Append(call).AppendLine(", cancellationToken);");
        s.AppendLine();
    }

    // What a form asks while it is filled in: the operation run on a copy, nothing saved (GeneratedOperations.Try).
    private static void AppendDryRun(StringBuilder s, OperatedAggregate aggregate, Operation operation)
    {
        var arguments = string.Join(", ", operation.Parameters.Select(static p => p.Name));
        var call = operation.Call.Replace("{0}", operation.Name + "(" + arguments + ")");

        s.Append("    /// <summary>Tries <c>").Append(operation.Name)
            .AppendLine("(…)</c> on a copy of the aggregate, its rules asked as a save asks them, and nothing written: what it would be refused for.</summary>");
        s.AppendLine("    /// <param name=\"id\">The id of the aggregate.</param>");
        if (aggregate.Versioned)
        {
            s.AppendLine("    /// <param name=\"version\">The version the caller read.</param>");
        }

        AppendParameterDocs(s, operation);
        s.AppendLine("    /// <param name=\"cancellationToken\">Cancels the try.</param>");
        s.AppendLine("    /// <returns>Every failure under the field it names; empty when the operation would go through.</returns>");
        AppendHidden(s);
        s.Append("    public static ").Append(FailuresTask).Append(" DryRun").Append(operation.Name).Append('(')
            .Append(aggregate.IdType).Append(" id, ");
        if (aggregate.Versioned)
        {
            s.Append("int version, ");
        }

        AppendParameters(s, operation);
        s.AppendLine("global::System.Threading.CancellationToken cancellationToken = default) =>");
        s.Append("        global::Rask.Data.GeneratedOperations.Try<").Append(aggregate.FullyQualifiedName).Append(">(id!, ")
            .Append(aggregate.Versioned ? "version" : "null").Append(", aggregate => ").Append(call).AppendLine(", cancellationToken);");
        s.AppendLine();
    }

    private static void AppendFactoryDryRun(StringBuilder s, OperatedAggregate aggregate, Operation operation)
    {
        var arguments = string.Join(", ", operation.Parameters.Select(static p => p.Name));
        var call = operation.Call.Replace("{0}", operation.Name + "(" + arguments + ")");

        s.Append("    /// <summary>Tries <c>").Append(operation.Name).Append("(…)</c>: the <c>").Append(aggregate.Name)
            .AppendLine("</c> built and its rules asked as a save asks them, and nothing written: what it would be refused for.</summary>");
        AppendParameterDocs(s, operation);
        s.AppendLine("    /// <param name=\"cancellationToken\">Cancels the try.</param>");
        s.AppendLine("    /// <returns>Every failure under the field it names; empty when the factory would go through.</returns>");
        AppendHidden(s);
        s.Append("    public static ").Append(FailuresTask).Append(" DryRun").Append(operation.Name).Append('(');
        AppendParameters(s, operation);
        s.AppendLine("global::System.Threading.CancellationToken cancellationToken = default) =>");
        s.Append("        global::Rask.Data.GeneratedOperations.TryBuild<").Append(aggregate.FullyQualifiedName)
            .Append(">(() => ").Append(call).AppendLine(", cancellationToken);");
        s.AppendLine();
    }

    private static void AppendHidden(StringBuilder s)
    {
        s.Append("    [global::System.CodeDom.Compiler.GeneratedCode(\"").Append(Tool).AppendLine("\", \"1\")]");
        s.AppendLine("    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
    }

    private static void AppendMay(StringBuilder s, Operation operation)
    {
        s.Append("    /// <summary>Whether the signed-in principal may run <c>").Append(operation.Name)
            .AppendLine("(…)</c>: holds one of the roles its <c>Configure</c> names, or any principal when it names none.</summary>");
        s.Append("    [global::System.CodeDom.Compiler.GeneratedCode(\"").Append(Tool).AppendLine("\", \"1\")]");
        s.Append("    public static bool May").Append(operation.Name).Append(" => ")
            .Append(operation.Roles is null ? "true" : "global::Rask.Data.GeneratedOperations.Admits(" + RolesLiteral(operation) + ")")
            .AppendLine(";");
        s.AppendLine();
    }

    private static string RolesLiteral(Operation operation) =>
        operation.Roles is { } roles ? SymbolDisplay.FormatLiteral(roles, quote: true) : "null";

    private static void AppendParameterDocs(StringBuilder s, Operation operation)
    {
        foreach (var parameter in operation.Parameters)
        {
            s.Append("    /// <param name=\"").Append(parameter.Name.TrimStart('@')).Append("\">What <c>").Append(operation.Name)
                .AppendLine("</c> is given.</param>");
        }
    }

    private static void AppendParameters(StringBuilder s, Operation operation)
    {
        foreach (var parameter in operation.Parameters)
        {
            // No `params`: the token comes after it.
            s.Append(parameter.Type).Append(' ').Append(parameter.Name);
            if (parameter.Default is { } value)
            {
                s.Append(" = ").Append(value);
            }

            s.Append(", ");
        }
    }

    private sealed record OperatedAggregate(
        string FullyQualifiedName,
        string Name,
        string? Namespace,
        string IdType,
        bool Versioned,
        EquatableArray<Operation> Operations,
        EquatableArray<OperationProblem> Problems);

    private sealed record Operation(string Name, string Call, string? Roles, EquatableArray<OperationParameter> Parameters, bool Factory);

    private sealed record OperationParameter(string Type, string Name, string? Default);

    private sealed record OperationProblem(string Id, string Method, string Clash, SymbolLocation? Location);

    private sealed record NamedRoles(bool Constant, string? Roles);
}
