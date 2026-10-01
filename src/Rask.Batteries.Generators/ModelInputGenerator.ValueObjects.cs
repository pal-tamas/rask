using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators;

public sealed partial class ModelInputGenerator
{
    // A value object built through a constructor or members generated code cannot reach directly: the accessors
    // it needs, and for a member-by-member build the helper that constructs it and writes each property.
    private static void EmitValueObjectBuild(StringBuilder s, ValueObjectShape shape)
    {
        switch (shape.Build)
        {
            case ModelValueObjectBuild.AccessorConstructor:
                s.AppendLine();
                s.Append("    [").Append(UnsafeAccessor).Append('(').Append(UnsafeAccessorKind).AppendLine(".Constructor)]");
                s.Append("    private static extern ").Append(shape.TypeName).Append(" __New").Append(shape.ModelName).Append('(')
                    .Append(string.Join(", ", shape.ConstructorParameterTypes.Select(static (t, i) =>
                        t + " p" + i.ToString(CultureInfo.InvariantCulture))))
                    .AppendLine(");");
                break;

            case ModelValueObjectBuild.AccessorMembers:
                if (!shape.PublicConstructor)
                {
                    s.AppendLine();
                    s.Append("    [").Append(UnsafeAccessor).Append('(').Append(UnsafeAccessorKind).AppendLine(".Constructor)]");
                    s.Append("    private static extern ").Append(shape.TypeName).Append(" __New").Append(shape.ModelName)
                        .AppendLine("();");
                }

                s.AppendLine();
                s.Append("    private static ").Append(shape.TypeName).Append(" __Build").Append(shape.ModelName).Append('(')
                    .Append(string.Join(", ", shape.Members.Select(static (m, i) =>
                        m.ValueTypeName + " p" + i.ToString(CultureInfo.InvariantCulture))))
                    .AppendLine(")");
                s.AppendLine("    {");
                s.Append("        var value = ")
                    .Append(shape.PublicConstructor ? "new " + shape.TypeName + "()" : "__New" + shape.ModelName + "()")
                    .AppendLine(";");
                var index = 0;
                foreach (var member in shape.Members)
                {
                    s.Append("        ").AppendLine(Assignment(
                        member.Write!, "value", member.Name, "p" + index.ToString(CultureInfo.InvariantCulture)));
                    index++;
                }

                s.AppendLine("        return value;");
                s.AppendLine("    }");

                foreach (var member in shape.Members)
                {
                    if (member.Write is { Kind: not ModelWriteKind.Public } write)
                    {
                        EmitAccessor(s, write, member.Name);
                    }
                }

                break;
        }
    }

    /// <summary>
    ///     The child reconcile: after this, the aggregate holds exactly what the posted list holds.
    /// </summary>
    /// <remarks>
    ///     Three rules, in the order they are safe to apply. A stored child whose id nobody posted is removed
    ///     first, so the list shrinks before anything is matched against it. A posted row whose id matches a
    ///     stored child updates that child. A posted row that matches nothing is a new child — including one
    ///     carrying an id that matches nothing, which is never followed: a forged id adds a row rather than
    ///     reaching one.
    /// </remarks>
    // Values have no identity, so there is nothing to match a posted row against and nothing to keep: the
    // stored collection is cleared and refilled from the form. That is the whole difference from a child
    // collection, which reconciles row by row so an untouched child keeps its key and its unposted columns.
    private static void EmitValueCollectionSync(StringBuilder s, ValueCollectionShape collection)
    {
        var source = collection.ThroughField
            ? "__Values_" + collection.Name + "(entity)"
            : "entity." + collection.Name;

        s.AppendLine();
        s.AppendLine("        {");
        s.Append("            var __values = ").Append(source)
            .Append(" as global::System.Collections.Generic.ICollection<")
            .Append(collection.ElementTypeName).AppendLine(">;");
        s.AppendLine();
        s.AppendLine("            if (__values is not null)");
        s.AppendLine("            {");
        s.AppendLine("                __values.Clear();");
        s.AppendLine();
        s.Append("                if (model.").Append(collection.Name).AppendLine(" is { } __posted)");
        s.AppendLine("                {");
        s.AppendLine("                    foreach (var __value in __posted)");
        s.AppendLine("                    {");
        s.Append("                        __values.Add(")
            .Append(collection.ElementModel is { } shape ? FromElementExpression("__value", shape) : "__value")
            .AppendLine(");");
        s.AppendLine("                    }");
        s.AppendLine("                }");
        s.AppendLine("            }");
        s.AppendLine("        }");
    }

    private static void EmitChildSync(StringBuilder s, ChildShape child)
    {
        var comparer = "global::System.Collections.Generic.EqualityComparer<" + child.ChildIdTypeName + ">.Default";
        var collection = child.ThroughField ? "__Field_" + child.Name + "(entity)" : "entity." + child.Name;
        var extensions = child.ChildModelName + "Extensions";

        s.AppendLine();
        s.AppendLine("        {");
        s.Append("            var __children = ").Append(collection).AppendLine(";");
        s.AppendLine();
        s.AppendLine("            if (__children is not null)");
        s.AppendLine("            {");
        s.Append("                var __posted = model.").Append(child.Name)
            .Append(" is { } __given ? __given : new global::System.Collections.Generic.List<")
            .Append(child.ChildModelName).AppendLine(">();");
        s.AppendLine();
        EmitStaleChildRemoval(s, comparer);
        s.AppendLine();
        s.AppendLine("                foreach (var __row in __posted)");
        s.AppendLine("                {");
        s.Append("                    ").Append(child.ChildTypeName).AppendLine("? __target = null;");
        s.AppendLine();
        s.AppendLine("                    if (__row.Id is { } __rowId)");
        s.AppendLine("                    {");
        s.AppendLine("                        foreach (var __stored in __children)");
        s.AppendLine("                        {");
        s.Append("                            if (").Append(comparer).AppendLine(".Equals(__rowId, __stored.Id))");
        s.AppendLine("                            {");
        s.AppendLine("                                __target = __stored;");
        s.AppendLine("                                break;");
        s.AppendLine("                            }");
        s.AppendLine("                        }");
        s.AppendLine("                    }");
        s.AppendLine();
        s.AppendLine("                    if (__target is null)");
        s.AppendLine("                    {");
        s.Append("                        __target = ").Append(extensions).AppendLine(".__NewChild();");
        s.AppendLine("                        __children.Add(__target);");
        s.AppendLine("                    }");
        s.AppendLine();
        s.Append("                    ").Append(extensions).AppendLine(".__Apply(__target, __row);");
        s.AppendLine("                }");
        s.AppendLine("            }");
        s.AppendLine("        }");
    }

    // A stored child whose id no posted row carries is removed: the posted list is what the aggregate holds.
    private static void EmitStaleChildRemoval(StringBuilder s, string comparer)
    {
        s.AppendLine("                foreach (var __stored in global::System.Linq.Enumerable.ToArray(__children))");
        s.AppendLine("                {");
        s.AppendLine("                    var __keep = false;");
        s.AppendLine();
        s.AppendLine("                    foreach (var __row in __posted)");
        s.AppendLine("                    {");
        s.Append("                        if (__row.Id is { } __rowId && ").Append(comparer)
            .AppendLine(".Equals(__rowId, __stored.Id))");
        s.AppendLine("                        {");
        s.AppendLine("                            __keep = true;");
        s.AppendLine("                            break;");
        s.AppendLine("                        }");
        s.AppendLine("                    }");
        s.AppendLine();
        s.AppendLine("                    if (!__keep)");
        s.AppendLine("                    {");
        s.AppendLine("                        __children.Remove(__stored);");
        s.AppendLine("                    }");
        s.AppendLine("                }");
    }

    private static void EmitAccessor(StringBuilder s, Write write, string memberName)
    {
        s.AppendLine();
        var attribute = write.Kind == ModelWriteKind.Setter
            ? "[" + UnsafeAccessor + "(" + UnsafeAccessorKind + ".Method, Name = \"set_" + memberName + "\")]"
            : "[" + UnsafeAccessor + "(" + UnsafeAccessorKind + ".Field, Name = \"<" + memberName + ">k__BackingField\")]";

        // A value type is reached by reference, or the accessor would write into a copy.
        var target = (write.ByRef ? "ref " : "") + write.TargetType;
        var signature = write.Kind == ModelWriteKind.Setter
            ? "void {0}(" + target + " target, " + write.ValueType + " value);"
            : "ref " + write.ValueType + " {0}(" + target + " target);";

        if (write.TypeParameters is null)
        {
            s.Append("    ").AppendLine(attribute);
            s.Append("    private static extern ").AppendLine(string.Format(CultureInfo.InvariantCulture, signature, write.AccessorName));
        }
        else
        {
            s.Append("    private static class ").Append(write.AccessorName).Append('<').Append(write.TypeParameters).Append('>')
                .AppendLine(write.Constraints);
            s.AppendLine("    {");
            s.Append("        ").AppendLine(attribute);
            s.Append("        public static extern ").AppendLine(string.Format(CultureInfo.InvariantCulture, signature, "Invoke"));
            s.AppendLine("    }");
        }
    }

    private static string Assignment(Write write, string receiver, string name, string value)
    {
        var target = (write.ByRef ? "ref " : "") + receiver;
        return write.Kind switch
        {
            ModelWriteKind.Public => receiver + "." + name + " = " + value + ";",
            ModelWriteKind.Setter when write.TypeParameters is null => write.AccessorName + "(" + target + ", " + value + ");",
            ModelWriteKind.Setter => write.AccessorName + "<" + write.TypeArguments + ">.Invoke(" + target + ", " + value + ");",
            _ when write.TypeParameters is null => write.AccessorName + "(" + target + ") = " + value + ";",
            _ => write.AccessorName + "<" + write.TypeArguments + ">.Invoke(" + target + ") = " + value + ";",
        };
    }

    // The value object built from one value per member, in whichever way this value object is built.
    private static string BuildExpression(ValueObjectShape shape, Func<ValueObjectMember, string> value)
    {
        string InConstructorOrder() =>
            string.Join(", ", shape.ConstructorOrder.Select(name => value(shape.Members.First(m => string.Equals(m.Name, name, StringComparison.Ordinal)))));

        return shape.Build switch
        {
            ModelValueObjectBuild.Constructor => "new " + shape.TypeName + "(" + InConstructorOrder() + ")",
            ModelValueObjectBuild.Initializer => "new " + shape.TypeName + " { " + string.Join(", ",
                shape.Members.Select(m => m.Name + " = " + value(m))) + " }",
            ModelValueObjectBuild.AccessorConstructor => "__New" + shape.ModelName + "(" + InConstructorOrder() + ")",
            _ => "__Build" + shape.ModelName + "(" + string.Join(", ", shape.Members.Select(value)) + ")",
        };
    }

    // `given` is a non-null nested model; `current` is the value object the aggregate holds now, which may be null (a
    // reference type, or a nullable property). Each member is the model's value when it gives one, else the current one,
    // else the member type's default — so a model that names only Amount still builds a whole Money.
    private static string MergeExpression(string given, string current, ValueObjectShape shape, bool currentNullable, ref int local)
    {
        var currentMayBeNull = currentNullable || !shape.IsValueType;
        var access = currentMayBeNull ? "?." : ".";
        var counter = local;

        string Member(ValueObjectMember member)
        {
            var currentMember = current + access + member.Name;
            var fallback = currentMayBeNull && !member.Nullable ? " ?? default(" + member.ValueTypeName + ")!" : "";

            switch (member.ValueObject)
            {
                case null:
                    return "(" + given + "." + member.Name + " ?? " + currentMember + fallback + ")";

                case { SingleValue: true } single:
                {
                    var inner = "__v" + (counter++).ToString(CultureInfo.InvariantCulture);
                    return "(" + given + "." + member.Name + " is { } " + inner + " ? " +
                           BuildExpression(single, _ => inner) + " : " + currentMember + fallback + ")";
                }

                default:
                {
                    var inner = "__v" + (counter++).ToString(CultureInfo.InvariantCulture);
                    var merged = MergeExpression(inner, currentMember, member.ValueObject, currentMayBeNull || member.Nullable, ref counter);
                    return "(" + given + "." + member.Name + " is { } " + inner + " ? " + merged + " : " + currentMember + fallback + ")";
                }
            }
        }

        var built = BuildExpression(shape, Member);
        local = counter;
        return built;
    }

    // What one element is carried as on the form model, declared INSIDE the model class.
    private static string ElementModelType(ValueCollectionShape collection) =>
        collection.ElementModel switch
        {
            null => collection.ElementTypeName,
            { SingleValue: true } single => single.Members[0].ModelType,
            { } nested => nested.ModelName,
        };

    // The same, named from outside the model class — __Fill and __Apply live in the extensions class.
    private static string ElementModelType(ValueCollectionShape collection, string modelType) =>
        collection.ElementModel is { SingleValue: false } nested
            ? modelType + "." + nested.ModelName
            : ElementModelType(collection);

    // One element, rebuilt from its form model. Unlike a single value object there is nothing to merge with:
    // a value in a collection has no identity, so nothing addresses the element that was there before. A
    // member the form did not set therefore takes its type's default, which is what an unbound field means.
    private static string FromElementExpression(string source, ValueObjectShape shape) =>
        shape.SingleValue
            ? BuildExpression(shape, member => Fallback(source, member))
            : BuildExpression(shape, member => member.ValueObject is null
                ? Fallback(source + "." + member.Name, member)
                : FromElementExpression(source + "." + member.Name, member.ValueObject));

    private static string Fallback(string access, ValueObjectMember member) =>
        "(" + access + " ?? default(" + member.ValueTypeName + ")!)";

    // The model's copy of a value object: the value itself for a one-value one, a nested model otherwise.
    private static string ToModelExpression(string source, ValueObjectShape shape, bool nullable, string modelType)
    {
        var mayBeNull = nullable || !shape.IsValueType;

        if (shape.SingleValue)
        {
            return source + (mayBeNull ? "?." : ".") + shape.Members[0].Name;
        }

        var build = "new " + modelType + "." + shape.ModelName + " { " + string.Join(", ",
            shape.Members.Select(m => m.Name + " = " + (m.ValueObject is null
                ? source + "." + m.Name
                : ToModelExpression(source + "." + m.Name, m.ValueObject, m.Nullable, modelType)))) + " }";

        return mayBeNull ? "(" + source + " is null ? null : " + build + ")" : build;
    }
}
