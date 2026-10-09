using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators;

/// <summary>
/// Gives every <c>Rask.Data.Aggregate&lt;TId&gt;</c> a form-shaped companion — <c>ProductModel</c> for <c>Product</c> — and
/// the writes that take it: <c>Product.Create(id, model)</c>, <c>Product.Create(model)</c> where a key
/// can be produced without the caller, <c>Product.Update(id, model)</c>, <c>Product.Update(id, apply)</c>
/// and <c>Product.Delete(id, version)</c> — unless the aggregate declares <c>Deletes = Deletion.None</c>. Every write takes an optional <c>apply</c> (values that do not come
/// from the form) where it builds or edits a row, and an optional <c>db</c> to join a caller's context.
/// </summary>
/// <remarks>
/// <para>
/// The model is a plain mutable class a form binds to, carrying the entity's validation attributes. The
/// writes go through <c>Rask.Data.GeneratedModelWrites</c>, so the change tracker — and with it the audit,
/// soft-delete and domain-event interceptors — sees every one.
/// </para>
/// <para>
/// <b>The model carries no key.</b> It is what a form posts back, so a key on it would be one the client
/// chooses. The id travels beside it — <c>Update(id, model)</c> — and a create takes none.
/// </para>
/// <para>
/// <b>An entity keeps its private setters and needs no <c>partial</c>.</b> Values are written through
/// <c>[UnsafeAccessor]</c> declarations: a direct, compile-time-bound call into the private member, with no
/// reflection for the trimmer to break. A member declared on a generic base is reached through a generic
/// accessor class carrying the base's type parameters AND their constraints, because the runtime matches the
/// accessor against the member's open signature and the compiler checks the constraints of every type the
/// accessor names. Value objects with non-public constructors or setters are rebuilt the same way.
/// </para>
/// </remarks>
[Generator]
public sealed partial class ModelInputGenerator : IIncrementalGenerator
{
    /// <summary>The suffix the generated model takes after the entity's name.</summary>
    internal const string ModelSuffix = GeneratedModelShape.ModelSuffix;

    // Mirrors Rask.Data.ModelWrites. A generator targets netstandard2.0 and never loads the runtime
    // assembly, so the values are restated here and pinned against the enum by a test.
    private const int NoWrites = 0;
    private const int CreateWrite = 1;
    private const int UpdateWrite = 2;
    internal const int AllWrites = CreateWrite | UpdateWrite;

    // Mirrors Rask.Data.Deletion.None, restated and pinned the same way.
    internal const int NoDeletion = 2;

    private const string DataAnnotationsNamespace = "System.ComponentModel.DataAnnotations";
    private const string ValidationAttribute = "System.ComponentModel.DataAnnotations.ValidationAttribute";
    private const string UnsafeAccessor = "global::System.Runtime.CompilerServices.UnsafeAccessor";
    private const string UnsafeAccessorKind = "global::System.Runtime.CompilerServices.UnsafeAccessorKind";

    private static readonly SymbolDisplayFormat TypeFormat = GeneratedModelShape.TypeFormat;

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var entities = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { BaseList.Types.Count: > 0 },
                static (ctx, ct) => GetEntity(ctx, ct))
            .Where(static entity => entity is not null)
            .Select(static (entity, _) => entity!);

        context.RegisterSourceOutput(
            entities.Collect().Combine(ReadFacesOnly.Of(context)).Combine(FormSeams(context)),
            static (spc, pair) =>
            {
                if (!pair.Left.Right)
                {
                    Emit(spc, pair.Left.Left, pair.Right);
                }
            });
    }

    // ---- discovery ------------------------------------------------------------------------------

    // ---- emit -----------------------------------------------------------------------------------

    private static void Emit(SourceProductionContext context, ImmutableArray<Entity> candidates, Seams seams)
    {
        if (candidates.IsDefaultOrEmpty)
        {
            return;
        }

        // Distinct because a partial entity contributes one candidate per declaration with a base list.
        var entities = candidates.Distinct().OrderBy(static e => e.FullyQualifiedName, StringComparer.Ordinal).ToList();

        // A child model exists to carry its ROOT's form — it is the element type of the list the root's model
        // holds — so it follows the root rather than deciding for itself. A child of a root that wants no form
        // gets no model either; a child held by two roots keeps one as long as either still has a form.
        var childrenWithAForm = new HashSet<string>(
            entities
                .Where(static e => !e.IsChild && e.Writes != NoWrites)
                .SelectMany(static e => e.Children.Select(static c => c.ChildTypeName)),
            StringComparer.Ordinal);

        var reportedMisses = new HashSet<RuleMiss>();

        foreach (var candidate in entities)
        {
            // A child's form surface is the ROOT's, always — its own const is reported by RASK091 and then
            // genuinely ignored, rather than half-obeyed into a root model holding a type nobody generated.
            var entity = candidate;
            if (candidate.IsChild)
            {
                entity = candidate with
                {
                    Writes = childrenWithAForm.Contains(candidate.FullyQualifiedName) ? AllWrites : NoWrites,
                };
            }

            switch (entity.Refusal)
            {
                case Refusal.Nested:
                    context.ReportDiagnostic(Diagnostic.Create(
                        Rask083, entity.Location?.ToLocation(), entity.Name, entity.RefusalDetail));
                    continue;
                case Refusal.Clash:
                    context.ReportDiagnostic(Diagnostic.Create(
                        Rask082, entity.Location?.ToLocation(), entity.Name, entity.RefusalDetail));
                    continue;
            }

            ReportShapeProblems(context, entity);
            ReportRuleMisses(context, entity, reportedMisses);
            entity = entity with
            {
                AnnouncesFieldRules = seams.FieldRules,
                DeclaresUniqueRules = entity.DeclaresUniqueRules && seams.StoreRules,
            };

            var hint = entity.FullyQualifiedName.Replace("global::", "") + ModelSuffix + ".g.cs";
            context.AddSource(hint, SourceText.From(Render(entity), Encoding.UTF8));
        }
    }

    // ---- the incremental model ------------------------------------------------------------------

    private enum Refusal
    {
        None,
        Nested,
        Clash,
    }

    // Who produces the key of a row Create(model) inserts. See KeySourceOf.
    private enum KeySource
    {
        None,
        Caller,
        Store,
        Guid,
    }

    private sealed record Entity(
        string FullyQualifiedName,
        string Name,
        string Namespace,
        string Accessibility,
        string? IdTypeName,
        bool Constructible,
        bool Versioned,
        Write? KeyWrite,
        KeySource KeySource,
        string? KeyFactory,
        bool KeyIsReference,
        SymbolLocation? Location,
        Refusal Refusal,
        string? RefusalDetail,
        EquatableArray<Member> Members,
        EquatableArray<ValueObjectShape> ValueObjects,
        bool IsChild,
        EquatableArray<ChildShape> Children,
        EquatableArray<string> AggregateReferences,
        EquatableArray<string> UnsyncableChildren,
        EquatableArray<ValueCollectionShape> ValueCollections,
        int Writes,
        bool DeclaresWrites,
        bool Deletable,
        bool DeclaresUniqueRules = false,
        bool AnnouncesFieldRules = true)
    {
        public static Entity Refused(string fullyQualifiedName, string name, SymbolLocation? location, Refusal refusal, string detail) =>
            new(fullyQualifiedName, name, "", "public", null, false, false, null, KeySource.None, null, false, location, refusal, detail,
                new EquatableArray<Member>([]), new EquatableArray<ValueObjectShape>([]), false,
                new EquatableArray<ChildShape>([]), new EquatableArray<string>([]), new EquatableArray<string>([]),
                new EquatableArray<ValueCollectionShape>([]), AllWrites, false, true);
    }

    // A collection of values on the form model. The element is carried as its nested model when it is a value
    // object, exactly as a single value object is, so a form binds the same shape either way.
    private sealed record ValueCollectionShape(
        string Name,
        string ElementTypeName,
        ValueObjectShape? ElementModel,
        bool ThroughField,
        string? FieldName,
        string FieldTypeName);

    /// <summary>One child collection, as the generator needs it: what to emit, and what to write it through.</summary>
    private sealed record ChildShape(
        string Name,
        string ChildTypeName,
        string ChildModelName,
        string ChildIdTypeName,
        bool ThroughField,
        string? FieldName,
        string FieldTypeName);

    private sealed record Member(
        string Name,
        ModelMemberRole Role,
        string ModelType,
        bool Nullable,
        ValueObjectShape? ValueObject,
        Write? Write,
        EquatableArray<string> Attributes,
        EquatableArray<string> Checks);

    private sealed record Write(
        ModelWriteKind Kind,
        string AccessorName,
        string TargetType,
        string ValueType,
        string? TypeParameters,
        string? TypeArguments,
        string Constraints,
        bool ByRef);

    private sealed record ValueObjectShape(
        string ModelName,
        string TypeName,
        ModelValueObjectBuild Build,
        EquatableArray<string> ConstructorOrder,
        EquatableArray<string> ConstructorParameterTypes,
        bool PublicConstructor,
        bool IsValueType,
        bool SingleValue,
        EquatableArray<ValueObjectMember> Members,
        ValueRule Rule = ValueRule.None,
        EquatableArray<RuleMiss> RuleMisses = default);

    private sealed record ValueObjectMember(
        string Name,
        string ModelType,
        bool Nullable,
        ValueObjectShape? ValueObject,
        EquatableArray<string> Attributes,
        string ValueTypeName,
        Write? Write);
}
