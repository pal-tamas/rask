using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators;

public sealed partial class ModelInputGenerator
{
    // RASK081 said this before the writes were dropped; a retired id is never recycled, so the rule returned as RASK086.
    internal static readonly DiagnosticDescriptor Rask086 = new(
        "RASK086",
        "Aggregate has no parameterless constructor, so Create is not generated",
        "'{0}' declares a constructor that takes arguments, so it has no parameterless one and no generated "
        + "'{0}.Create' — from a '{0}Model' or a 'p => …' — exists; declare no constructor and build it in a "
        + "static factory instead, or insert one you built with '{0}.Create(entity)'",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        description: "Every generated Create — and a new form model's defaults — starts from an empty aggregate, "
                     + "which needs a constructor that takes nothing. An aggregate that declares no constructor has "
                     + "one for free; domain creation belongs in a static factory. Everything that works on a row that "
                     + "already exists — the model itself, Update and Delete — is still generated.",
        helpLinkUri: DiagnosticHelp.Link("RASK086"));

    internal static readonly DiagnosticDescriptor Rask087 = new(
        "RASK087",
        "Aggregate reaches across a boundary",
        "'{0}.{1}' is {3}, which is an aggregate of its own — a boundary {0} may not reach across, so Rask "
        + "never loads, carries, saves or deletes it with {0}; {4}",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "An aggregate is a consistency boundary, and the border is what one aggregate can SEE of "
                     + "another: holding nothing but an id is what makes crossing a boundary by accident "
                     + "impossible. Another AGGREGATE has its own version, its own soft delete and its own "
                     + "reads and writes, so a form post on this one must never add to it and never delete "
                     + "from it. Nothing is lost by holding the id: the generated read face carries the "
                     + "navigation the write model is not allowed to have, so the join you wanted is still one "
                     + "expression — it just cannot be reached from the side that saves.",
        helpLinkUri: DiagnosticHelp.Link("RASK087"));

    internal static readonly DiagnosticDescriptor Rask088 = new(
        "RASK088",
        "Child collection cannot be synced",
        "'{0}.{1}' holds children, but Rask cannot find a collection to write: its type is not an "
        + "ICollection<T> and '{0}' has {2}, so the model can show these children but a save cannot add or "
        + "remove one; expose the collection as ICollection<T>, or keep it in a single backing field",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        description: "A child collection on the model is editable: what the form posts is what the aggregate "
                     + "holds afterwards. Writing it needs something to add to and remove from — the property "
                     + "itself when it is an ICollection<T>, or the one field behind it when it hands out a "
                     + "read-only view. With neither, a save would silently keep whatever was stored, which "
                     + "looks exactly like a form that did not submit.",
        helpLinkUri: DiagnosticHelp.Link("RASK088"));

    internal static readonly DiagnosticDescriptor Rask082 = new(
        "RASK082",
        "A type already has the generated model's name",
        "'{1}' already exists beside the entity '{0}', so Rask cannot generate its form model or the "
        + "Create, Update and Delete that take it; rename the existing type, declare it "
        + "'partial' to extend the generated one",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "Every Rask.Data.Aggregate<TId> gets a generated {Entity}Model in its own namespace. A hand-written, "
                     + "non-partial type of that name would collide with it as CS0101, a message that names "
                     + "neither the generator nor the way out — so the generator stands down and says why instead.",
        helpLinkUri: DiagnosticHelp.Link("RASK082"));

    internal static readonly DiagnosticDescriptor Rask083 = new(
        "RASK083",
        "Nested entity gets no generated model",
        "'{0}' is declared inside '{1}', so no '{0}Model' is generated for it; declare the entity at "
        + "namespace level to get one",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        description: "The generated model and its writes are emitted beside the entity, as siblings in its "
                     + "namespace. An entity nested in another type has no such place, so it is mapped as usual "
                     + "but gets no form model.",
        helpLinkUri: DiagnosticHelp.Link("RASK083"));

    internal static readonly DiagnosticDescriptor Rask091 = new(
        "RASK091",
        "A child cannot choose its own form writes",
        "'{0}' is a child of another aggregate, so its 'Writes' const is ignored — a child model exists to "
        + "carry the ROOT's form and is generated with it; put the const on the aggregate that holds '{0}'",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        description: "ModelWrites narrows the form surface of an AGGREGATE. A child has no writes of its own "
                     + "to narrow — it is created, changed and removed through its root — and its model is "
                     + "part of the root's, which is what a form posts. Honouring the const here would leave "
                     + "the root's model holding a list of a child model that was never generated, so it is "
                     + "reported rather than obeyed.",
        helpLinkUri: DiagnosticHelp.Link("RASK091"));

    internal static readonly DiagnosticDescriptor Rask102 = new(
        "RASK102",
        "A value object's Validate is not the shape of a rule, so no form runs it",
        "'{0}' on value object '{1}' is not run by any form because {2} — a rule is "
        + "'public static IEnumerable<string> Validate({3} value)', or the same returning "
        + "ValueTask<IEnumerable<string>> or Task<IEnumerable<string>>",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        description: "A value object that holds one value states its rule by convention: a public static, "
                     + "non-generic Validate taking exactly the value it holds and returning the messages. A form "
                     + "bound to the aggregate's generated model then runs it with nothing written on the field. "
                     + "A Validate of any other shape is not run, and a rule that never runs looks exactly like "
                     + "one that ran and passed.",
        helpLinkUri: DiagnosticHelp.Link("RASK102"));

    // Once per method, however many aggregates hold the value object.
    private static void ReportRuleMisses(SourceProductionContext context, Entity entity, HashSet<RuleMiss> reported)
    {
        var held = entity.Members.Select(static m => m.ValueObject)
            .Concat(entity.ValueObjects.SelectMany(static v => v.Members.Select(static m => m.ValueObject)));

        foreach (var miss in held.Where(static v => v is not null).SelectMany(static v => v!.RuleMisses).Where(reported.Add))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Rask102, miss.Location?.ToLocation(), miss.Signature, miss.ValueObject, miss.Reason, miss.Held));
        }
    }

    private static void ReportShapeProblems(SourceProductionContext context, Entity entity)
    {
        if (!entity.Constructible)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rask086, entity.Location?.ToLocation(), entity.Name));
        }

        if (entity.IsChild && entity.DeclaresWrites)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rask091, entity.Location?.ToLocation(), entity.Name));
        }

        foreach (var held in entity.AggregateReferences)
        {
            var parts = held.Split('|');
            var (property, target, idType, many) = (parts[0], parts[1], parts[2], parts[3] is "many");

            // The diagnosis is one rule; the fix is not. A single reference becomes an id on THIS side and
            // a collection becomes an id on the OTHER, so telling an author to add '{target}Id' to an
            // aggregate that needs the parent's id instead would name the wrong half of the relationship.
            context.ReportDiagnostic(Diagnostic.Create(
                Rask087,
                entity.Location?.ToLocation(),
                entity.Name,
                property,
                target,
                many ? $"a collection of '{target}'" : $"a reference to '{target}'",
                many
                    ? $"let each '{target}' hold {entity.Name}'s id and read them back through "
                      + $"{target}.Where(…), whose navigation those ids infer, or derive '{target}' from "
                      + $"Entity<TId> if they are genuinely part of {entity.Name}"
                    : $"hold its id instead — '{idType} {property}Id' — and read the join through "
                      + $"{entity.Name}.Where(…), where it is inferred back as '{property}', or derive "
                      + $"'{target}' from Entity<TId> if it is genuinely part of {entity.Name}"));
        }

        foreach (var unsyncable in entity.UnsyncableChildren)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Rask088,
                entity.Location?.ToLocation(),
                entity.Name,
                unsyncable,
                "no single field of that collection type"));
        }
    }
}
