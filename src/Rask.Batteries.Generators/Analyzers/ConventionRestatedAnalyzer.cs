using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators.Analyzers;

/// <summary>
///     RASK192 — a line of an entity's configuration that says what Rask's conventions (or EF Core's) already say: a
///     <c>ToTable("fuelings")</c>, <c>HasColumnName("pistol_id")</c>, <c>HasDatabaseName("ix_fuelings_pistol_id")</c>,
///     <c>HasName("pk_fuelings")</c>, <c>HasConstraintName("fk_fuelings_pistol_id")</c>, <c>[Table("fuelings")]</c> or
///     <c>[Column("pistol_id")]</c> that names it exactly as Rask would — and an <c>OnDelete</c>, <c>HasMaxLength</c>,
///     <c>HasPrecision</c>, <c>IsRequired()</c>, <c>HasKey(x =&gt; x.Id)</c>, <c>ValueGeneratedNever()</c>,
///     <c>IsConcurrencyToken()</c> or <c>HasConversion</c> equal to the default (<c>ConventionRestatedAnalyzer.Defaults.cs</c>).
///     Deleting it changes nothing.
/// </summary>
/// <remarks>
///     <para>
///         A suggestion, faded as unneeded, with a quick fix (and Fix all) that deletes it. Every name is compared with
///         <see cref="StoreNames" /> — the source Rask.Data names the model by — and only where the analyzer can be sure
///         of what Rask names it: an entity's own table (not a hierarchy's derived type, not an owned type), and a
///         constraint over the entity's own columns. A project that changes the pattern — <c>TableNames</c>,
///         <c>ColumnNames</c> or <c>ConstraintNames</c> anywhere in it — gets no naming report at all; one that changes the
///         delete rule (<c>DeleteBehavior(children: …, others: …)</c>) in a way this cannot read gets no <c>OnDelete</c> report.
///     </para>
///     <para>
///         The one rule for every default Rask has: a <see cref="Restated" /> kind each, so the next default this reports
///         (an <c>OnDelete</c> equal to Rask's, a <c>HasMaxLength</c> equal to the value object's) is one more kind and one
///         more check, under the same id and the same fix.
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class ConventionRestatedAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The diagnostic's id.</summary>
    public const string Id = "RASK192";

    private const string EfCore = "Microsoft.EntityFrameworkCore";
    private const string Schema = "System.ComponentModel.DataAnnotations.Schema";

    private static readonly DiagnosticDescriptor Rule = new(
        Id,
        "The configuration says what Rask's conventions already say",
        "'{0}' says what Rask does anyway: {1} — delete it",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Rask names every table, column and constraint itself — delivery_notes, pistol_id, "
                     + "ix_fuelings_pistol_id — and gives every column its value object's length, every key its generation, "
                     + "every child its cascade. A line in Configure, or a [Table]/[Column], that says exactly that "
                     + "is a line to keep in step with nothing: delete it, and the model is the same.",
        helpLinkUri: DiagnosticHelp.Link(Id),
        customTags: WellKnownDiagnosticTags.Unnecessary);

    /// <summary>What a reported line restates.</summary>
    private enum Restated
    {
        Table,
        Column,
        Key,
        Index,
        ForeignKey,
        DeleteBehavior,
        Facet,
        Required,
        PrimaryKey,
        KeyGeneration,
        ConcurrencyToken,
        Conversion,
    }

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(start =>
        {
            // A project that restates the pattern names nothing as Rask does: say nothing about its names.
            var names = !ChangesThePattern(start.Compilation, start.CancellationToken);
            var deletes = DeleteRule.Of(start.Compilation, start.CancellationToken);

            start.RegisterOperationAction(c => AnalyzeCall(c, names, deletes), OperationKind.Invocation);
            if (names)
            {
                start.RegisterSymbolAction(AnalyzeAttributes, SymbolKind.NamedType, SymbolKind.Property);
            }
        });
    }

    // By the text, once per compilation: cheaper than binding every call, and a false alarm only keeps the analyzer quiet.
    private static bool ChangesThePattern(Compilation compilation, System.Threading.CancellationToken cancellationToken) =>
        compilation.SyntaxTrees.Any(tree => tree.GetText(cancellationToken).ToString() is var text &&
            (text.Contains(".TableNames(") || text.Contains(".ColumnNames(") || text.Contains(".ConstraintNames(")));

    private static void AnalyzeCall(OperationAnalysisContext context, bool names, DeleteRule? deletes)
    {
        var call = (IInvocationOperation)context.Operation;
        var method = call.TargetMethod.ReducedFrom ?? call.TargetMethod;
        if (method.ContainingNamespace?.ToDisplayString() is not { } space || !(Is(space, EfCore) || space.StartsWith(EfCore + ".", StringComparison.Ordinal)) ||
            call.Syntax is not InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access } syntax)
        {
            return;
        }

        var found = (names ? Named(call, method.Name) : null) ?? Defaulted(call, method.Name, deletes);
        if (found is { } restated)
        {
            var span = TextSpan.FromBounds(access.OperatorToken.SpanStart, syntax.Span.End);
            var shown = syntax.SyntaxTree.GetText(context.CancellationToken).ToString(TextSpan.FromBounds(access.Name.SpanStart, syntax.Span.End));
            context.ReportDiagnostic(Diagnostic.Create(
                Rule, Location.Create(syntax.SyntaxTree, span), Properties(restated.Kind), shown, restated.Message));
        }
    }

    // A name stated exactly as Rask gives it.
    private static Finding? Named(IInvocationOperation call, string method)
    {
        if (Stated(call) is not { } stated)
        {
            return null;
        }

        var expected = method switch
        {
            "ToTable" => Table(call),
            "HasColumnName" => Column(call),
            "HasDatabaseName" => Index(call),
            "HasName" => Key(call),
            "HasConstraintName" => ForeignKey(call),
            _ => null,
        };

        return expected is { } rasks && string.Equals(rasks.Name, stated, StringComparison.Ordinal)
            ? new Finding(rasks.Kind, Said(rasks))
            : null;
    }

    private static void AnalyzeAttributes(SymbolAnalysisContext context)
    {
        var (entity, expected) = context.Symbol switch
        {
            INamedTypeSymbol type when IsRootEntity(type) => (type, StoreNames.Table(type.Name)),
            IPropertySymbol { ContainingType: { } type } property when AggregateShape.IsEntity(type) => (type, StoreNames.Column([property.Name])),
            _ => (null, null),
        };

        if (entity is null || expected is null)
        {
            return;
        }

        var wanted = context.Symbol is INamedTypeSymbol ? "TableAttribute" : "ColumnAttribute";
        foreach (var attribute in context.Symbol.GetAttributes())
        {
            if (attribute.AttributeClass is { } type && Is(type.Name, wanted) && Is(type.ContainingNamespace?.ToDisplayString(), Schema) &&
                attribute.NamedArguments.IsEmpty &&
                attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is string stated &&
                string.Equals(stated, expected, StringComparison.Ordinal) &&
                attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken) is AttributeSyntax syntax)
            {
                var kind = context.Symbol is INamedTypeSymbol ? Restated.Table : Restated.Column;
                context.ReportDiagnostic(Diagnostic.Create(
                    Rule, syntax.GetLocation(), Properties(kind), $"[{wanted.Substring(0, wanted.Length - "Attribute".Length)}(\"{stated}\")]",
                    Said(new Expected(kind, expected))));
            }
        }
    }

    private static ImmutableDictionary<string, string?> Properties(Restated kind) =>
        ImmutableDictionary<string, string?>.Empty.Add("Restated", kind.ToString());

    private static string Said(Expected expected) => expected.Kind switch
    {
        Restated.Table => "the table of its class, in snake case and plural",
        Restated.Column => "the column of its property, in snake case",
        Restated.Key => "pk_ and its table",
        Restated.Index => "ix_ (uq_ when unique), its table and its columns",
        _ => "fk_, its table and its columns",
    };

    // The one name the call states: a constant, its only argument.
    private static string? Stated(IInvocationOperation call) =>
        call.TargetMethod.Parameters.Length == ReceiverCount(call) + 1
        && Argument(call, 0) is { ConstantValue: { HasValue: true, Value: string stated } }
            ? stated
            : null;

    private static Expected? Table(IInvocationOperation call) =>
        Builder(Receiver(call)) is { } entity && IsRootEntity(entity) ? new Expected(Restated.Table, StoreNames.Table(entity.Name)) : null;

    private static Expected? Column(IInvocationOperation call)
    {
        if (Down(Receiver(call), "Property") is not { } property || PathOf(property) is not { } path)
        {
            return null;
        }

        return new Expected(Restated.Column, StoreNames.Column(path));
    }

    private static Expected? Index(IInvocationOperation call)
    {
        var unique = false;
        var receiver = Receiver(call);
        while (receiver is IInvocationOperation step && !Is(step.TargetMethod.Name, "HasIndex"))
        {
            unique |= Is(step.TargetMethod.Name, "IsUnique") && !(Argument(step, 0)?.ConstantValue is { HasValue: true, Value: false });
            receiver = Receiver(step);
        }

        if (receiver is not IInvocationOperation index || Builder(Receiver(index)) is not { } entity || !IsEntityOwnTable(entity) ||
            Members(index, entity) is not { } columns || TableOf(call, entity) is not { } table)
        {
            return null;
        }

        var prefix = unique ? StoreNames.Unique : StoreNames.Index;
        return new Expected(Restated.Index, StoreNames.Constraint(prefix, table, columns));
    }

    private static Expected? Key(IInvocationOperation call)
    {
        if (Receiver(call) is not IInvocationOperation key || Builder(Receiver(key)) is not { } entity || !IsEntityOwnTable(entity) ||
            TableOf(call, entity) is not { } table)
        {
            return null;
        }

        return key.TargetMethod.Name switch
        {
            "HasKey" => new Expected(Restated.Key, StoreNames.Constraint(StoreNames.PrimaryKey, table, [])),
            "HasAlternateKey" when Members(key, entity) is { } columns => new Expected(Restated.Key, StoreNames.Constraint(StoreNames.Unique, table, columns)),
            _ => null,
        };
    }

    private static Expected? ForeignKey(IInvocationOperation call)
    {
        if (Down(Receiver(call), "HasForeignKey") is not { } foreignKey)
        {
            return null;
        }

        // The table holding the key is the one this Configure is about: the dependent's own.
        var root = Root(foreignKey);
        var dependent = foreignKey.TargetMethod.IsGenericMethod
            ? foreignKey.TargetMethod.TypeArguments[0]
            : (Receiver(foreignKey)?.Type as INamedTypeSymbol)?.TypeArguments.LastOrDefault();
        return root is { } entity && SymbolEqualityComparer.Default.Equals(entity, dependent) && IsEntityOwnTable(entity) &&
               Members(foreignKey, entity) is { } columns && TableOf(call, entity) is { } table
            ? new Expected(Restated.ForeignKey, StoreNames.Constraint(StoreNames.ForeignKey, table, columns))
            : null;
    }

    // The receiver of a call, written as an instance call or as an extension method's first argument.
    private static IOperation? Receiver(IInvocationOperation call) =>
        Unwrapped(call.Instance ?? (call.TargetMethod.IsExtensionMethod && call.Arguments.Length > 0 ? call.Arguments[0].Value : null));

    private static IOperation? Unwrapped(IOperation? operation)
    {
        while (operation is IConversionOperation { IsImplicit: true } conversion)
        {
            operation = conversion.Operand;
        }

        return operation;
    }

    // The call's `ordinal`th argument after its receiver, whether it is an instance or an extension method.
    private static IOperation? Argument(IInvocationOperation call, int ordinal)
    {
        var at = ordinal + ReceiverCount(call);
        return call.Arguments.FirstOrDefault(a => a.Parameter?.Ordinal == at)?.Value;
    }

    // The call named `name` this chain was made on: `b.Property(x => x.A).HasMaxLength(9)` for HasColumnName.
    private static IInvocationOperation? Down(IOperation? receiver, string name)
    {
        while (receiver is IInvocationOperation step)
        {
            if (Is(step.TargetMethod.Name, name))
            {
                return step;
            }

            receiver = Receiver(step);
        }

        return null;
    }

    // The entity of the EntityTypeBuilder<T> a chain starts at.
    private static INamedTypeSymbol? Root(IInvocationOperation call)
    {
        IOperation? receiver = call;
        while (receiver is IInvocationOperation step)
        {
            receiver = Receiver(step);
        }

        return Builder(receiver);
    }

    private static INamedTypeSymbol? Builder(IOperation? operation) =>
        operation?.Type is INamedTypeSymbol { Name: "EntityTypeBuilder", TypeArguments.Length: 1 } builder &&
        builder.TypeArguments[0] is INamedTypeSymbol entity &&
        Is(builder.ContainingNamespace?.ToDisplayString(), EfCore + ".Metadata.Builders")
            ? entity
            : null;

    // The property's path: its name, after the value objects (ComplexProperty) holding it. Null inside an owned type, a
    // JSON column, or anything this cannot follow.
    private static List<string>? PathOf(IInvocationOperation property)
    {
        if (MemberOf(property, 0) is not { } name)
        {
            return null;
        }

        var receiver = Receiver(property);
        if (Builder(receiver) is { } entity)
        {
            return AggregateShape.IsEntity(entity) ? [name] : null;
        }

        // A value object's member: `b.ComplexProperty(x => x.Km).Property(k => k.Start)`, or the same inside its lambda.
        var complex = receiver as IInvocationOperation ?? Holder(receiver);
        if (complex is null || !Is(complex.TargetMethod.Name, "ComplexProperty") || PathOf(complex) is not { } outer)
        {
            return null;
        }

        outer.Add(name);
        return outer;
    }

    // `k` in `b.ComplexProperty(x => x.Km, k => k.Property(…))`: the ComplexProperty call whose lambda it is.
    private static IInvocationOperation? Holder(IOperation? receiver)
    {
        if (receiver is not IParameterReferenceOperation parameter)
        {
            return null;
        }

        IOperation? walk = parameter;
        while (walk is not null && walk is not IAnonymousFunctionOperation)
        {
            walk = walk.Parent;
        }

        var lambda = walk as IAnonymousFunctionOperation;
        if (lambda is null || !SymbolEqualityComparer.Default.Equals(lambda.Symbol.Parameters.FirstOrDefault(), parameter.Parameter))
        {
            return null;
        }

        walk = lambda.Parent;
        while (walk is not null && walk is not IInvocationOperation)
        {
            walk = walk.Parent;
        }

        return walk as IInvocationOperation;
    }

    // The member a call names in its argument: `x => x.A`, or "A".
    private static string? MemberOf(IInvocationOperation call, int ordinal) =>
        Unwrapped(Argument(call, ordinal)) switch
        {
            { ConstantValue: { HasValue: true, Value: string name } } => name,
            var lambda when Returned(Lambda(lambda)) is { } body =>
                Unwrapped(body) is IPropertyReferenceOperation { Instance: IParameterReferenceOperation } member ? member.Property.Name : null,
            _ => null,
        };

    // What an expression lambda returns: `x.A` of `x => x.A`.
    private static IOperation? Returned(IAnonymousFunctionOperation? lambda) =>
        lambda?.Body.Operations.Length == 1 && lambda.Body.Operations[0] is IReturnOperation { ReturnedValue: { } body } ? body : null;

    private static IAnonymousFunctionOperation? Lambda(IOperation? operation) => Unwrapped(operation) switch
    {
        IDelegateCreationOperation { Target: IAnonymousFunctionOperation lambda } => lambda,
        IAnonymousFunctionOperation lambda => lambda,
        _ => null,
    };

    // The columns an index, key or foreign key names, as Rask names each: `x => x.A`, `x => new { x.A, x.B }`, "A", "B".
    private static List<string>? Members(IInvocationOperation call, INamedTypeSymbol entity)
    {
        var names = new List<string>();
        var argument = Unwrapped(Argument(call, 0));
        if (Returned(Lambda(argument)) is { } body)
        {
            var returned = Unwrapped(body);
            var members = returned is IAnonymousObjectCreationOperation anonymous
                ? anonymous.Initializers.Select(static i => i is ISimpleAssignmentOperation a ? Unwrapped(a.Value) : null)
                : [returned];
            foreach (var member in members)
            {
                if (member is not IPropertyReferenceOperation { Instance: IParameterReferenceOperation } reference || HoldsSeveral(reference.Property.Type))
                {
                    return null;
                }

                names.Add(reference.Property.Name);
            }
        }
        else if (argument is IArrayCreationOperation { Initializer: { } strings })
        {
            foreach (var element in strings.ElementValues)
            {
                if (element.ConstantValue is not { HasValue: true, Value: string name } ||
                    (entity.GetMembers(name).OfType<IPropertySymbol>().FirstOrDefault() is { } property && HoldsSeveral(property.Type)))
                {
                    return null;
                }

                names.Add(name);
            }
        }
        else
        {
            return null;
        }

        return names.Count == 0 ? null : [.. names.Select(static n => StoreNames.Column([n]))];
    }

    // A value object of several members is several columns, which no index is named after.
    private static bool HoldsSeveral(ITypeSymbol type) =>
        type is INamedTypeSymbol { IsRecord: true } record &&
        record.InstanceConstructors.Any(static c => c.Parameters.Length > 1 && !c.IsImplicitlyDeclared);

    // The table Rask gives the entity, or the literal ToTable names in the same method; null when it is named otherwise.
    private static string? TableOf(IInvocationOperation call, INamedTypeSymbol entity)
    {
        var body = call.Syntax.Ancestors().FirstOrDefault(static n => n is BaseMethodDeclarationSyntax or LocalFunctionStatementSyntax);
        var model = call.SemanticModel;
        if (body is null || model is null)
        {
            return StoreNames.Table(entity.Name);
        }

        foreach (var toTable in body.DescendantNodes().OfType<InvocationExpressionSyntax>()
                     .Where(static i => i.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "ToTable" }))
        {
            if (model.GetOperation(toTable) is IInvocationOperation table && SymbolEqualityComparer.Default.Equals(Builder(Receiver(table)), entity))
            {
                return Stated(table);
            }
        }

        return StoreNames.Table(entity.Name);
    }

    // A Rask entity with a table of its own: not a type in its base's table (Rask's own Entity/Aggregate bases aside),
    // not generic.
    private static bool IsRootEntity(INamedTypeSymbol type) =>
        AggregateShape.IsEntity(type) && !type.IsGenericType && type.BaseType is { } baseType &&
        (!AggregateShape.IsEntity(baseType) || Is(baseType.ContainingNamespace?.ToDisplayString(), "Rask.Data"));

    private static bool IsEntityOwnTable(INamedTypeSymbol type) => IsRootEntity(type);

    private static bool Is(string? text, string expected) => string.Equals(text, expected, StringComparison.Ordinal);

    // An extension method takes its receiver as its first parameter.
    private static int ReceiverCount(IInvocationOperation call) => call.TargetMethod.IsExtensionMethod ? 1 : 0;

    private sealed record Expected(Restated Kind, string Name);

    // A line found restating a default: what it restates, and how the message says it.
    private sealed record Finding(Restated Kind, string Message);
}
