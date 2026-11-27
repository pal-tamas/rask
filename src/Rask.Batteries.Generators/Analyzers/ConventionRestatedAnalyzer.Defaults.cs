using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators.Analyzers;

// RASK192's defaults other than names: a call in Configure that sets what Rask (or EF Core) sets anyway. Each is reported
// only where the analyzer can prove the model the same without it, and only where the call can come out of its chain —
// a step that hands back what it was called on, or the last step of a statement.
public sealed partial class ConventionRestatedAnalyzer
{
    private const string PropertyBuilder = "PropertyBuilder";

    // The calls an entity's Configure can make that tell EF Core how a key's values are made: one beside a
    // ValueGeneratedNever() makes it say something.
    private static readonly string[] KeyGenerators =
        ["HasDefaultValue", "HasDefaultValueSql", "HasValueGenerator", "ValueGeneratedOnAdd", "UseIdentityColumn", "HasComputedColumnSql"];

    private static Finding? Defaulted(IInvocationOperation call, string method, DeleteRule? deletes)
    {
        if (!Removable(call))
        {
            return null;
        }

        return method switch
        {
            "OnDelete" => OnDelete(call, deletes),
            "HasMaxLength" or "HasPrecision" => Facet(call, method),
            "IsRequired" => Required(call),
            "HasKey" => PrimaryKey(call),
            "ValueGeneratedNever" => KeyGeneration(call),
            "IsConcurrencyToken" => ConcurrencyToken(call),
            "HasConversion" => Conversion(call),
            _ => null,
        };
    }

    // Out of its chain without changing what the chain is: the last step of a statement, or one that hands back the
    // builder it was called on.
    private static bool Removable(IInvocationOperation call) =>
        call.Parent is IExpressionStatementOperation ||
        (Receiver(call)?.Type is { } receiver && SymbolEqualityComparer.Default.Equals(receiver, call.Type));

    // `.OnDelete(DeleteBehavior.Cascade)` on a child's key to its aggregate, or what every other key does.
    private static Finding? OnDelete(IInvocationOperation call, DeleteRule? rule)
    {
        if (rule is null || Argument(call, 0)?.ConstantValue is not { HasValue: true, Value: int stated } ||
            Relationship(Receiver(call)) is not { } relationship)
        {
            return null;
        }

        var expected = relationship.Child
            ? rule.Children
            : rule.Others ?? relationship.KeyRequired switch
            {
                true => rule.Cascade,
                false => rule.ClientSetNull,
                null => (int?)null,
            };

        if (expected != stated)
        {
            return null;
        }

        var said = relationship.Child ? "a child's key cascades from its aggregate" : "what a delete does to every other key";
        return new Finding(Restated.DeleteBehavior, said);
    }

    // The relationship a chain configures, when it is one the analyzer can be sure of: whether its key holds a child to
    // its aggregate, and — for any other — whether the key is required. Null for anything else in the chain.
    private static RelationshipShape? Relationship(IOperation? receiver)
    {
        var steps = new List<IInvocationOperation>();
        while (receiver is IInvocationOperation step)
        {
            if (step.TargetMethod.Name is not ("HasOne" or "HasMany" or "WithOne" or "WithMany" or "HasForeignKey" or "HasConstraintName"))
            {
                return null;
            }

            steps.Add(step);
            receiver = Receiver(step);
        }

        if (Builder(receiver) is not { } entity || steps.Count < 2 || !IsEntityOwnTable(entity))
        {
            return null;
        }

        var start = steps[steps.Count - 1];
        var with = steps[steps.Count - 2];
        var navigated = Explicit(start, 0) is not null;
        var related = start.TargetMethod.IsGenericMethod ? start.TargetMethod.TypeArguments[0] as INamedTypeSymbol : null;
        if (related is null)
        {
            return null;
        }

        var foreignKey = steps.FirstOrDefault(static s => Is(s.TargetMethod.Name, "HasForeignKey"));
        return (start.TargetMethod.Name, with.TargetMethod.Name) switch
        {
            // b.HasMany(x => x.Lines).WithOne(): the aggregate's children, Rask's cascade.
            ("HasMany", "WithOne") when navigated && IsChild(related) => new RelationshipShape(true, null),
            ("HasMany", "WithOne") when !navigated && !Holds(entity, related) => new RelationshipShape(false, RequiredKey(foreignKey, related)),

            // b.HasOne(x => x.Order).WithMany(o => o.Lines): the same key, from the child.
            ("HasOne", "WithMany") when Explicit(with, 0) is not null && IsChild(entity) => new RelationshipShape(true, null),
            ("HasOne", "WithMany") when Explicit(with, 0) is null && !Holds(related, entity) => new RelationshipShape(false, RequiredKey(foreignKey, entity)),
            _ => null,
        };
    }

    // The argument the call was given, not one its parameter's default stands in for: WithMany() names no navigation.
    private static IOperation? Explicit(IInvocationOperation call, int ordinal)
    {
        var at = ordinal + ReceiverCount(call);
        return call.Arguments.FirstOrDefault(a => a.Parameter?.Ordinal == at && a.ArgumentKind != ArgumentKind.DefaultValue)?.Value;
    }

    // A child: an entity that is no aggregate.
    private static bool IsChild(INamedTypeSymbol type) => AggregateShape.IsEntity(type) && !AggregateShape.IsAggregate(type);

    // Whether `owner` has a collection of `held` EF Core could pair with an unnamed navigation, which the analyzer cannot follow.
    private static bool Holds(INamedTypeSymbol owner, INamedTypeSymbol held) =>
        owner.GetMembers().OfType<IPropertySymbol>().Any(p =>
            p.Type is INamedTypeSymbol { IsGenericType: true } collection &&
            collection.AllInterfaces.Concat([collection]).Any(face =>
                face is { Name: "IEnumerable", TypeArguments.Length: 1 } && SymbolEqualityComparer.Default.Equals(face.TypeArguments[0], held)));

    // Whether the key HasForeignKey(x => x.PistolId) names is required — the property cannot be null — or null when there is none to read.
    private static bool? RequiredKey(IInvocationOperation? foreignKey, INamedTypeSymbol dependent) =>
        foreignKey is not null && MemberOf(foreignKey, 0) is { } name &&
        Find(dependent, name) is { } property && property.Type is not ITypeParameterSymbol
            ? NonNullable(property)
            : null;

    // HasMaxLength(1000) / HasPrecision(18, 2) on a value object's column that declares exactly that.
    private static Finding? Facet(IInvocationOperation call, string method)
    {
        if (EntityProperty(call) is not { } held || !AggregateShape.IsEntity(held.Entity) ||
            held.Property.Type.WithNullableAnnotation(NullableAnnotation.NotAnnotated) is not INamedTypeSymbol valueObject ||
            AggregateShape.ValueBaseOf(valueObject) is not ("Text" or "Number") ||
            ValueObjectBaseArguments.Of(valueObject) is not { Count: > 0 } declared ||
            HasFacetAttribute(held.Property) ||
            Constants(call) is not { Count: > 0 } given)
        {
            return null;
        }

        var text = ValueObjectBaseArguments.IsText(valueObject);
        var same = Is(method, "HasMaxLength") ? SameLength(text, declared, given) : SamePrecision(text, declared, given);
        if (!same)
        {
            return null;
        }

        var facet = text ? "length" : "precision";
        return new Finding(Restated.Facet, "the column takes the " + facet + " its value object " + valueObject.Name + " declares");
    }

    private static bool SameLength(bool text, Dictionary<string, int> declared, List<int> given) =>
        text && given.Count == 1 && declared.TryGetValue("MaxLength", out var most) && most > 0 && most == given[0];

    // HasPrecision(p) leaves the scale Rask gave; HasPrecision(p, s) states it, which Rask gives as the declared scale or 0.
    private static bool SamePrecision(bool text, Dictionary<string, int> declared, List<int> given)
    {
        if (text || !declared.TryGetValue("Precision", out var precision) || precision != given[0])
        {
            return false;
        }

        var scale = declared.TryGetValue("Scale", out var declaredScale) ? declaredScale : 0;
        return given.Count == 1 || (given.Count == 2 && given[1] == scale);
    }

    private static bool HasFacetAttribute(IPropertySymbol property) =>
        property.GetAttributes().Any(static a => a.AttributeClass?.Name is "MaxLengthAttribute" or "StringLengthAttribute" or "PrecisionAttribute");

    // The call's arguments after its receiver, when every one is an int constant.
    private static List<int>? Constants(IInvocationOperation call)
    {
        var values = new List<int>();
        foreach (var argument in call.Arguments.Where(a => a.Parameter?.Ordinal >= ReceiverCount(call)))
        {
            if (argument.Value.ConstantValue is not { HasValue: true, Value: int value })
            {
                return null;
            }

            values.Add(value);
        }

        return values;
    }

    // IsRequired() on a property that cannot be null: EF Core makes it required by its type.
    private static Finding? Required(IInvocationOperation call)
    {
        if (!Is(call.TargetMethod.ContainingType?.Name, PropertyBuilder) || !SaysTrue(call) ||
            EntityProperty(call) is not { } held || held.Property.Type is ITypeParameterSymbol || !NonNullable(held.Property))
        {
            return null;
        }

        return new Finding(Restated.Required, "a property that cannot be null is required");
    }

    // The call's one bool, left out (its default, true) or true.
    private static bool SaysTrue(IInvocationOperation call) =>
        Argument(call, 0) is not { } said || said.ConstantValue is { HasValue: true, Value: true };

    private static bool NonNullable(IPropertySymbol property) =>
        property.Type.IsValueType
            ? property.Type.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T
            : property.NullableAnnotation == NullableAnnotation.NotAnnotated;

    // HasKey(x => x.Id), the whole statement, on a Rask entity's own table: EF Core keys it by Id anyway.
    private static Finding? PrimaryKey(IInvocationOperation call)
    {
        if (call.Parent is not IExpressionStatementOperation || Builder(Receiver(call)) is not { } entity || !IsRootEntity(entity) ||
            !Is(MemberOf(call, 0), "Id") || Find(entity, "Id") is not { Type: not INamedTypeSymbol { IsTupleType: true } } ||
            KeysOtherwise(entity))
        {
            return null;
        }

        return new Finding(Restated.PrimaryKey, "EF Core keys an entity by its Id");
    }

    // A [Key] on a property, or a [PrimaryKey] or [Keyless] on the type: the key is not Id's by convention.
    private static bool KeysOtherwise(INamedTypeSymbol entity)
    {
        for (var type = entity; type is not null; type = type.BaseType)
        {
            if (type.GetAttributes().Any(static a => a.AttributeClass?.Name is "PrimaryKeyAttribute" or "KeylessAttribute") ||
                type.GetMembers().OfType<IPropertySymbol>().Any(static p => p.GetAttributes().Any(static a => a.AttributeClass?.Name is "KeyAttribute")))
            {
                return true;
            }
        }

        return false;
    }

    // ValueGeneratedNever() on the Id of an entity keyed by a Guid, a string or a typed id over one: Rask leaves it to the entity.
    private static Finding? KeyGeneration(IInvocationOperation call)
    {
        if (EntityProperty(call) is not { } held || !Is(held.Property.Name, "Id") || !IsRootEntity(held.Entity) ||
            !AggregateShape.TryGetIdType(held.Entity, out var id) || !MadeByTheEntity(id) || SaysHowKeysAreMade(call))
        {
            return null;
        }

        return new Finding(Restated.KeyGeneration, "a key that is no number is the entity's to set");
    }

    private static bool MadeByTheEntity(ITypeSymbol? id) =>
        id is { SpecialType: SpecialType.System_String } ||
        id is { Name: "Guid", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } } ||
        TypedIdShape.Of(id) is TypedIdValue.Guid or TypedIdValue.String ||
        TypedIdShape.OfReferenced(id) is TypedIdValue.Guid or TypedIdValue.String;

    // A default, a generator or an identity stated in the same method: the key's generation is then said elsewhere too.
    private static bool SaysHowKeysAreMade(IInvocationOperation call)
    {
        var body = call.Syntax.Ancestors().FirstOrDefault(static n => n is BaseMethodDeclarationSyntax or LocalFunctionStatementSyntax) ?? call.Syntax.SyntaxTree.GetRoot();
        return body.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
            .Any(static m => KeyGenerators.Contains(m.Name.Identifier.ValueText, System.StringComparer.Ordinal));
    }

    // IsConcurrencyToken() on the Version of an aggregate that says IVersioned: Rask makes it the token.
    private static Finding? ConcurrencyToken(IInvocationOperation call)
    {
        if (!Is(call.TargetMethod.ContainingType?.Name, PropertyBuilder) || !SaysTrue(call) ||
            Down(Receiver(call), "Property") is not { } property || Builder(Receiver(property)) is not { } entity ||
            !Is(MemberOf(property, 0), "Version") || !AggregateShape.HasVersion(entity))
        {
            return null;
        }

        return new Finding(Restated.ConcurrencyToken, "an IVersioned aggregate's Version is its concurrency token");
    }

    // HasConversion(v => v.Value, v => new X(v)) on a typed id or a one-value value object, which Rask converts so already.
    private static Finding? Conversion(IInvocationOperation call)
    {
        if (call.TargetMethod.Parameters.Length != ReceiverCount(call) + 2 ||
            EntityProperty(call) is not { } held || !AggregateShape.IsEntity(held.Entity) ||
            held.Property.Type.WithNullableAnnotation(NullableAnnotation.NotAnnotated) is not INamedTypeSymbol type ||
            !ConvertedByRask(type) ||
            !ReadsValue(Lambda(Argument(call, 0))) || !Builds(Lambda(Argument(call, 1)), type))
        {
            return null;
        }

        return new Finding(Restated.Conversion, (TypedIdShape.Is(type) ? "a typed id" : "a one-value value object") + " is stored as the value it holds");
    }

    // A typed id, or a value object Rask maps to its one value — not a moment, a day or a secret, which it stores its own way.
    private static bool ConvertedByRask(INamedTypeSymbol type) =>
        TypedIdShape.Is(type) ||
        (AggregateShape.ValueBaseOf(type) is "Text" or "Number" or "Integer" or "Value" && !AggregateShape.IsSecret(type) && !AggregateShape.IsPassword(type));

    // `v => v.Value`.
    private static bool ReadsValue(IAnonymousFunctionOperation? lambda) =>
        lambda is { Symbol.Parameters.Length: 1 } &&
        lambda.Symbol.Parameters[0] is var parameter &&
        Unwrapped(Returned(lambda)) is IPropertyReferenceOperation { Property.Name: "Value", Instance: IParameterReferenceOperation reference } &&
        SymbolEqualityComparer.Default.Equals(reference.Parameter, parameter);

    // `v => new X(v)`.
    private static bool Builds(IAnonymousFunctionOperation? lambda, INamedTypeSymbol type) =>
        lambda is { Symbol.Parameters.Length: 1 } &&
        lambda.Symbol.Parameters[0] is var parameter &&
        Unwrapped(Returned(lambda)) is IObjectCreationOperation { Initializer: null, Arguments.Length: 1 } creation &&
        SymbolEqualityComparer.Default.Equals(creation.Type, type) &&
        Unwrapped(creation.Arguments[0].Value) is IParameterReferenceOperation reference &&
        SymbolEqualityComparer.Default.Equals(reference.Parameter, parameter);

    // The entity property `b.Property(x => x.A)` names, the chain made on an entity's own builder.
    private static (INamedTypeSymbol Entity, IPropertySymbol Property)? EntityProperty(IInvocationOperation call)
    {
        if (Down(Receiver(call), "Property") is not { } property || Builder(Receiver(property)) is not { } entity ||
            Unwrapped(Returned(Lambda(Argument(property, 0)))) is not IPropertyReferenceOperation { Instance: IParameterReferenceOperation } member)
        {
            return null;
        }

        return (entity, member.Property);
    }

    // The property `name` of `type` or a base of it.
    private static IPropertySymbol? Find(INamedTypeSymbol type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.GetMembers(name).OfType<IPropertySymbol>().FirstOrDefault(static p => !p.IsStatic) is { } property)
            {
                return property;
            }
        }

        return null;
    }

    // A relationship RASK192 can be sure of: whether its key holds a child, and whether it is required (null: unknown).
    private sealed record RelationshipShape(bool Child, bool? KeyRequired);
}
