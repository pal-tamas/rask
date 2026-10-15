using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators;

// A value object of several values the aggregate may be without: its nested model, left wholly empty, means
// "not given". Nothing is built for it and nothing in it is asked for; what the row holds there is cleared,
// unless that was empty too — and then the row is left exactly as it is.
public sealed partial class ModelInputGenerator
{
    private const string TextModelType = "string?";
    private const string FlagModelType = "bool?";

    private static string EmptyMethod(ValueObjectShape shape) => "__Empty_" + shape.ModelName;

    private static string KeptMethod(ValueObjectShape shape) => "__Kept_" + shape.ModelName;

    // "…is there, and holds something": the test a nested model passes before it is built or asked anything.
    private static string AndNotEmpty(string held, ValueObjectShape shape, bool nullable) =>
        nullable && !shape.SingleValue ? " && !" + EmptyMethod(shape) + "(" + held + ")" : "";

    // The slots that may be absent: on the aggregate, and inside any value object it holds.
    private static IEnumerable<ValueObjectShape> HeldWhereItMayBeAbsent(Entity entity) =>
        entity.Members
            .Where(static m => m.Role == ModelMemberRole.Value && m is { Nullable: true, ValueObject.SingleValue: false })
            .Select(static m => m.ValueObject!)
            .Concat(entity.ValueObjects.SelectMany(static v => v.Members)
                .Where(static m => m is { Nullable: true, ValueObject.SingleValue: false })
                .Select(static m => m.ValueObject!))
            .GroupBy(static shape => shape.ModelName, StringComparer.Ordinal)
            .Select(static group => group.First());

    // Those, and every value object of several values inside them: an empty one is empty all the way down.
    private static List<ValueObjectShape> AskedWhetherEmpty(Entity entity)
    {
        var asked = new List<ValueObjectShape>();

        void Add(ValueObjectShape shape)
        {
            if (shape.SingleValue || asked.Exists(known => string.Equals(known.ModelName, shape.ModelName, StringComparison.Ordinal)))
            {
                return;
            }

            asked.Add(shape);
            foreach (var inner in shape.Members.Select(static m => m.ValueObject).OfType<ValueObjectShape>())
            {
                Add(inner);
            }
        }

        foreach (var shape in HeldWhereItMayBeAbsent(entity))
        {
            Add(shape);
        }

        return asked;
    }

    // Empty is what a form leaves when nobody filled it: no value, blank text, a box nobody ticked. Every member
    // of a model is nullable, so each has that state; an unticked box says nothing either, since a box ticked
    // and unticked again writes `false`. Any other value — `0` included — is one somebody gave.
    private static void AppendEmptyChecks(StringBuilder s, Entity entity)
    {
        var modelType = FormModelType(entity);

        foreach (var shape in AskedWhetherEmpty(entity))
        {
            s.Append("    private static bool ").Append(EmptyMethod(shape)).Append('(').Append(modelType).Append('.')
                .Append(shape.ModelName).AppendLine(" model) =>");
            s.Append("        ").Append(string.Join(" && ", shape.Members.Select(EmptyTest))).AppendLine(";");
            s.AppendLine();
        }

        // What an empty model leaves in the row: the stored value object when that holds nothing either — so a
        // save that changes nothing writes nothing, and text stored as '' stays '' — and nothing otherwise.
        foreach (var shape in HeldWhereItMayBeAbsent(entity))
        {
            s.Append("    private static ").Append(shape.TypeName).Append("? ").Append(KeptMethod(shape)).Append('(')
                .Append(shape.TypeName).AppendLine("? stored) =>");
            s.Append("        stored is { } __s && ").Append(ToModelExpression("__s", shape, nullable: false, modelType))
                .Append(" is { } __m && ").Append(EmptyMethod(shape)).AppendLine("(__m) ? stored : null;");
            s.AppendLine();
        }
    }

    private static string EmptyTest(ValueObjectMember member, int index)
    {
        var access = "model." + member.Name;

        if (member.ValueObject is { SingleValue: false } inner)
        {
            var held = "__m" + index.ToString(CultureInfo.InvariantCulture);
            return "(" + access + " is not { } " + held + " || " + EmptyMethod(inner) + "(" + held + "))";
        }

        return member.ModelType switch
        {
            TextModelType => "string.IsNullOrWhiteSpace(" + access + ")",
            FlagModelType => access + " is not true",
            _ => access + " is null",
        };
    }

    // ---- what a form is told ----
    // A form asks by the type a field belongs to, and one nested model type can sit where it must be there and
    // where it may be absent. So the answer is given per SLOT: the model that holds the property says which of
    // its properties may be left wholly empty, and a form then asks nothing of an unfilled field under one.

    private sealed record OptionalSlotOwner(string Type, string Key, List<string> Slots);

    private static IEnumerable<OptionalSlotOwner> OptionalSlots(Entity entity)
    {
        var root = entity.Members
            .Where(static m => m.Role == ModelMemberRole.Value && m is { Nullable: true, ValueObject.SingleValue: false })
            .Select(static m => m.Name)
            .ToList();

        if (root.Count > 0)
        {
            yield return new OptionalSlotOwner(FormModelType(entity), "Root_", root);
        }

        foreach (var nested in entity.ValueObjects.Where(static v => !v.SingleValue))
        {
            var slots = nested.Members
                .Where(static m => m is { Nullable: true, ValueObject.SingleValue: false })
                .Select(static m => m.Name)
                .ToList();

            if (slots.Count > 0)
            {
                yield return new OptionalSlotOwner(FormModelType(entity) + "." + nested.ModelName, nested.ModelName + "_", slots);
            }
        }
    }

    private static void AppendOptionalRegistrations(StringBuilder s, List<OptionalSlotOwner> owners)
    {
        foreach (var owner in owners)
        {
            s.Append("        ").Append(RaskValidation).Append(".RegisterOptionalFields(typeof(").Append(owner.Type)
                .Append("), ").Append(owner.Key).AppendLine("IsOptional);");
        }
    }

    private static void AppendOptionalSlots(StringBuilder s, List<OptionalSlotOwner> owners)
    {
        foreach (var owner in owners)
        {
            s.AppendLine();
            s.Append("    private static bool ").Append(owner.Key).AppendLine("IsOptional(string property) =>");
            s.Append("        property is ").Append(string.Join(" or ", owner.Slots.Select(static slot => "\"" + slot + "\""))).AppendLine(";");
        }
    }
}
