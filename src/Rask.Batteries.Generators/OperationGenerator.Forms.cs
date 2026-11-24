using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators;

// F-43: a form straight over an operation — `Form.For(Supply.Change, id)[f => [ … ]]` — whose fields are the operation's
// parameters. Per operation: a model (`SupplyChangeInput`, a property per parameter, a value object of one value held
// as that value), its field shapes and required fields, the operation's dry run as the store's rules a form asks as
// each field is committed, and the `Form.For` overload that fills it from the aggregate and saves it through the entry.
public sealed partial class OperationGenerator
{
    private const string OperationFormType = "Rask.Core.Forms.OperationForm`1";
    private const string OperationFormFqn = "global::Rask.Core.Forms.OperationForm";
    private const string FailureFqn = "global::Rask.Wire.FieldFailure";
    private const string FailureListFqn = "global::System.Collections.Generic.List<global::Rask.Wire.FieldFailure>";
    private const string TokenFqn = "global::System.Threading.CancellationToken";
    private const string ValidationFqn = "global::Rask.Core.Forms.RaskValidation";
    private const string HiddenFqn = "[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]";

    // The most parameters an operation's form takes: its entry, id, version and token included, is a Func of at most 16.
    private const int MostFormParameters = 13;

    internal static readonly DiagnosticDescriptor Rask174 = new(
        "RASK174",
        "Two operations take the same parameters, so neither gets a Form.For",
        "'{0}' and another operation take the same parameters, so 'Form.For({0}, …)' could not tell them apart and "
        + "neither gets one — give one of them a value object of its own for what it takes",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Info,
        true,
        description: "Form.For(Supply.Change, id) picks the operation by the parameters of its generated entry, since a "
                     + "method group carries no name a call can be told by. Two operations whose entries take the same "
                     + "types — Rename(DriverName) and SetFullName(DriverName) — would make every call ambiguous, so "
                     + "neither gets a form; a value object per meaning (FullName) tells them apart.",
        helpLinkUri: DiagnosticHelp.Link("RASK174"));

    /// <summary>One parameter as a field of the operation's form, or null for a type no form field holds.</summary>
    /// <param name="Property">The field's name: the parameter's, capitalised, as the aggregate names what it sets.</param>
    /// <param name="PropertyType">What the field holds: the parameter's own type, or a value object's one value.</param>
    /// <param name="ValueObject">The value object the field is made into, or null for a value taken as it is.</param>
    /// <param name="Optional">Whether the parameter may be null.</param>
    /// <param name="Required">Whether the field must be filled.</param>
    /// <param name="Text">Whether the value object holds a text.</param>
    /// <param name="Fill">What the aggregate holds for it, read off <c>aggregate</c>, or null when it names nothing alike.</param>
    private sealed record FormField(
        string Property, string PropertyType, string? ValueObject, bool Optional, bool Required, bool Text, string? Fill);

    private static FormField? FieldOf(IParameterSymbol parameter, INamedTypeSymbol aggregate)
    {
        var property = Capitalised(parameter.Name);
        var optional = parameter.NullableAnnotation == NullableAnnotation.Annotated ||
                       parameter.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
        var held = aggregate.GetMembers(property).OfType<IPropertySymbol>()
            .FirstOrDefault(p => !p.IsStatic && SymbolEqualityComparer.Default.Equals(p.Type.WithNullableAnnotation(NullableAnnotation.None), parameter.Type.WithNullableAnnotation(NullableAnnotation.None)));

        if (parameter.Type is INamedTypeSymbol named && OneValueOf(named) is { } inner)
        {
            var text = inner.SpecialType == SpecialType.System_String;
            var innerType = inner.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString(TypeFormat);
            var access = held?.NullableAnnotation == NullableAnnotation.Annotated ? "?.Value" : ".Value";
            var fill = held is null ? null : "aggregate." + property + access;
            return new FormField(
                property,
                text ? "string?" : innerType + "?",
                named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                optional,
                !optional && !(text && AggregateShape.AllowsEmptyText(named)),
                text,
                fill);
        }

        if (!IsFormValue(parameter.Type))
        {
            return null;
        }

        var type = parameter.Type.ToDisplayString(TypeFormat);
        var isString = parameter.Type.SpecialType == SpecialType.System_String;
        // A plain text is the domain's to refuse (its Refuse, its attributes), told by the dry run: no required rule of the form's own.
        return new FormField(property, type, null, optional, Required: false, isString, held is null ? null : "aggregate." + property);
    }

    // The one value a value object declared from Text, Number, Whole, Day or Moment holds: its positional parameter.
    private static ITypeSymbol? OneValueOf(INamedTypeSymbol type) =>
        AggregateShape.DeclaredValueBaseOf(type) is "Text" or "Number" or "Whole" or "Day" or "Moment"
            ? type.InstanceConstructors
                .Where(c => c.Parameters.Length == 1 && !SymbolEqualityComparer.Default.Equals(c.Parameters[0].Type, type))
                .Select(static c => c.Parameters[0].Type)
                .FirstOrDefault()
            : null;

    // A value a field holds as it is: text, a number, a day, a bool, an enum, an id — any value type, and a string.
    private static bool IsFormValue(ITypeSymbol type) =>
        type.SpecialType == SpecialType.System_String || type.IsValueType || type.TypeKind == TypeKind.Error;

    private static string Capitalised(string name) =>
        name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name.Substring(1);

    // The entry a form calls, as the delegate a method group of it converts to.
    private static string? EntryDelegate(OperatedAggregate aggregate, Operation operation)
    {
        if (operation.Parameters.Count > MostFormParameters || operation.Parameters.Any(static p => p.Field is null))
        {
            return null;
        }

        var types = new List<string>();
        if (!operation.Factory)
        {
            types.Add(aggregate.IdType);
            if (aggregate.Versioned)
            {
                types.Add("int");
            }
        }

        types.AddRange(operation.Parameters.Select(static p => p.Type));
        types.Add(TokenFqn);
        types.Add(operation.Factory ? TaskFqn + "<" + aggregate.IdType + ">" : TaskFqn);
        return "global::System.Func<" + string.Join(", ", types) + ">";
    }

    private static Dictionary<string, int> SignatureCounts(IEnumerable<OperatedAggregate> all)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var aggregate in all.Where(static a => a.Forms))
        {
            foreach (var operation in aggregate.Operations)
            {
                if (EntryDelegate(aggregate, operation) is { } entry)
                {
                    counts[entry] = counts.TryGetValue(entry, out var count) ? count + 1 : 1;
                }
            }
        }

        return counts;
    }

    private static string? RenderForms(OperatedAggregate aggregate, Dictionary<string, int> signatures, List<string> ambiguous)
    {
        var formed = Formed(aggregate, signatures, ambiguous);
        var deleting = aggregate.Dialog && aggregate.Delete is not null;
        if (formed.Count == 0 && !deleting)
        {
            return null;
        }

        var s = new StringBuilder();
        s.AppendLine("// <auto-generated/>");
        s.AppendLine("#nullable enable");
        if (aggregate.Namespace is { } ns)
        {
            s.Append("namespace ").Append(ns).AppendLine(";");
        }

        s.AppendLine();
        foreach (var (operation, _) in formed)
        {
            AppendInput(s, aggregate, operation);
        }

        var forms = aggregate.Name + "OperationForms";
        s.Append("/// <summary>The forms over the operations of <c>").Append(aggregate.Name).AppendLine("</c>: <c>Form.For(…)</c>.</summary>");
        s.AppendLine(HiddenFqn);
        s.Append("public static class ").AppendLine(forms);
        s.AppendLine("{");
        if (formed.Count > 0)
        {
            AppendRegistrations(s, aggregate, formed);
        }

        if (deleting)
        {
            AppendDialogDelete(s, aggregate);
        }

        // A classic extension of the seed, which the chain analyzer knows as an opening that takes the owed steps itself.
        foreach (var (operation, entry) in formed)
        {
            AppendFor(s, aggregate, operation, entry);
        }

        foreach (var (operation, _) in formed)
        {
            AppendReader(s, aggregate, operation);
            AppendShapesAndRequired(s, operation);
            AppendRules(s, aggregate, operation);
        }

        s.AppendLine("}");
        return s.ToString();
    }

    // The operations a form can be told apart for: every parameter one a field holds, and no other entry alike.
    private static List<(Operation Operation, string Entry)> Formed(
        OperatedAggregate aggregate, Dictionary<string, int> signatures, List<string> ambiguous)
    {
        var formed = new List<(Operation Operation, string Entry)>();
        foreach (var operation in aggregate.Operations)
        {
            if (EntryDelegate(aggregate, operation) is not { } entry)
            {
                continue;
            }

            if (signatures[entry] > 1)
            {
                ambiguous.Add(aggregate.Name + "." + operation.Name);
                continue;
            }

            formed.Add((operation, entry));
        }

        return formed;
    }

    private static void AppendRegistrations(StringBuilder s, OperatedAggregate aggregate, List<(Operation Operation, string Entry)> formed)
    {
        s.AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]");
        s.AppendLine("    internal static void Register()");
        s.AppendLine("    {");
        foreach (var (operation, _) in formed)
        {
            var input = InputName(aggregate, operation);
            s.Append("        ").Append(ValidationFqn).Append(".RegisterFieldShapes(typeof(").Append(input).Append("), ").Append(operation.Name).AppendLine("ShapeOf);");
            s.Append("        ").Append(ValidationFqn).Append(".RegisterRequiredFields(typeof(").Append(input).Append("), ").Append(operation.Name).AppendLine("Required);");
            s.Append("        ").Append(ValidationFqn).Append(".RegisterStoreRules(typeof(").Append(input).Append("), static _ => new ").Append(operation.Name).AppendLine("Rules());");
        }

        s.AppendLine("    }");
        s.AppendLine();
    }

    // `Ui.EditorDialog.In("edit")[…].Delete(Driver.Delete)`: the dialog's Delete button, over the record its key names —
    // read at the version stored now, tried first, then deleted at that version.
    private static void AppendDialogDelete(StringBuilder s, OperatedAggregate aggregate)
    {
        var entry = "global::System.Func<" + aggregate.IdType + ", " + (aggregate.Versioned ? "int, " : "") + TokenFqn + ", " + TaskFqn + ">";
        var parse = string.Equals(aggregate.IdType, "string", StringComparison.Ordinal)
            ? "editing is { } id"
            : aggregate.IdType.TrimEnd('?') + ".TryParse(editing, out var id)";
        var find = "await global::Rask.Data.GeneratedModelWrites.Find<" + aggregate.FullyQualifiedName + ">(id!, cancellationToken) is { } aggregate";
        var version = aggregate.Versioned ? "aggregate.Version, " : "";

        s.Append("    /// <summary>A Delete button in the dialog for the <c>").Append(aggregate.Name)
            .AppendLine("</c> its key names: tried first, asked, then deleted through the entry.</summary>");
        s.AppendLine("    /// <param name=\"dialog\">The editor dialog.</param>");
        s.Append("    /// <param name=\"delete\">The delete: <c>").Append(aggregate.Name).AppendLine(".Delete</c>.</param>");
        s.AppendLine("    /// <returns>The same dialog.</returns>");
        s.Append("    public static global::Rask.UiEditorDialog Delete(this global::Rask.UiEditorDialog dialog, ").Append(entry).AppendLine(" delete)");
        s.AppendLine("    {");
        s.AppendLine("        dialog.Deleting(");
        s.Append("            async (editing, cancellationToken) => ").Append(parse).Append(" && ").Append(find)
            .Append(" ? await ").Append(aggregate.FullyQualifiedName).Append(".DryRunDelete(id, ").Append(version)
            .AppendLine("cancellationToken).ConfigureAwait(false) : global::System.Array.Empty<" + FailureFqn + ">(),");
        s.AppendLine("            async (editing, cancellationToken) =>");
        s.AppendLine("            {");
        s.Append("                if (").Append(parse).Append(" && ").Append(find).AppendLine(")");
        s.AppendLine("                {");
        s.Append("                    await delete(id, ").Append(version).AppendLine("cancellationToken).ConfigureAwait(false);");
        s.AppendLine("                }");
        s.AppendLine("            },");
        s.Append("            static () => ").Append(aggregate.FullyQualifiedName).AppendLine(".MayDelete);");
        s.AppendLine("        return dialog;");
        s.AppendLine("    }");
        s.AppendLine();
    }

    private static string InputName(OperatedAggregate aggregate, Operation operation) => aggregate.Name + operation.Name + "Input";

    private static void AppendInput(StringBuilder s, OperatedAggregate aggregate, Operation operation)
    {
        var input = InputName(aggregate, operation);
        s.Append("/// <summary>The fields of a form over <c>").Append(aggregate.Name).Append('.').Append(operation.Name)
            .AppendLine("(…)</c>: one per parameter.</summary>");
        s.AppendLine(HiddenFqn);
        s.Append("public sealed class ").AppendLine(input);
        s.AppendLine("{");
        if (!operation.Factory)
        {
            s.AppendLine("    /// <summary>Starts the fields of the aggregate with <paramref name=\"id\" />, read at <paramref name=\"version\" />.</summary>");
            s.AppendLine("    /// <param name=\"id\">The aggregate the form changes.</param>");
            s.AppendLine("    /// <param name=\"version\">The version it was read at.</param>");
            s.Append("    public ").Append(input).Append('(').Append(aggregate.IdType).AppendLine(" id, int version) => (Id, Version) = (id, version);");
            s.AppendLine();
            s.AppendLine("    /// <summary>The aggregate the form changes: never a field.</summary>");
            s.Append("    public ").Append(aggregate.IdType).AppendLine(" Id { get; }");
            s.AppendLine();
            s.AppendLine("    /// <summary>The version the aggregate was read at: never a field.</summary>");
            s.AppendLine("    public int Version { get; }");
            s.AppendLine();
        }

        foreach (var parameter in operation.Parameters)
        {
            var field = parameter.Field!;
            s.Append("    /// <summary>What <c>").Append(operation.Name).Append("</c> is given as <c>").Append(parameter.Name.TrimStart('@'))
                .AppendLine("</c>.</summary>");
            s.Append("    public ").Append(field.PropertyType).Append(' ').Append(field.Property).Append(" { get; set; }");
            if (field.Text && !field.PropertyType.EndsWith("?", StringComparison.Ordinal))
            {
                s.Append(" = \"\";");
            }

            s.AppendLine();
            s.AppendLine();
        }

        s.AppendLine("}");
        s.AppendLine();
    }

    private static void AppendFor(StringBuilder s, OperatedAggregate aggregate, Operation operation, string entry)
    {
        var input = InputName(aggregate, operation);
        var form = OperationFormFqn + "<" + input + ">";

        s.Append("        /// <summary>A form over <c>").Append(aggregate.Name).Append('.').Append(operation.Name)
            .AppendLine("(…)</c>, whose fields are its parameters: filled from what is stored, checked as it is filled in, saved through the entry.</summary>");
        s.AppendLine("        /// <param name=\"operation\">The operation: <c>" + aggregate.Name + "." + operation.Name + "</c>.</param>");
        if (!operation.Factory)
        {
            s.AppendLine("        /// <param name=\"id\">The aggregate it changes.</param>");
        }

        s.AppendLine("        /// <returns>The form; its fields go in its indexer.</returns>");
        s.AppendLine("        /// <param name=\"form\">The form seed: <c>Form</c>.</param>");
        s.Append("        public static ").Append(form).Append(" For(this global::Rask.Core.RaskSeed_Form form, ").Append(entry).Append(" operation");
        if (!operation.Factory)
        {
            s.Append(", ").Append(aggregate.IdType).Append(" id");
        }

        s.AppendLine(") =>");
        s.Append("            global::Rask.Core.Forms.OperationForms.Of<").Append(input).AppendLine(">(");
        s.Append("                ").Append(operation.Factory ? "null" : "id").AppendLine(",");
        AppendLoad(s, aggregate, operation, input);
        AppendSubmit(s, aggregate, operation);
        s.Append("                static () => ").Append(aggregate.FullyQualifiedName).Append(".May").Append(operation.Name).AppendLine(");");
        s.AppendLine();
    }

    // A factory's form starts empty; an operation's is read from the aggregate, each field from what it holds alike.
    private static void AppendLoad(StringBuilder s, OperatedAggregate aggregate, Operation operation, string input)
    {
        if (operation.Factory)
        {
            s.Append("                static _ => global::System.Threading.Tasks.Task.FromResult<").Append(input).Append("?>(new ").Append(input).AppendLine("()),");
            return;
        }

        s.Append("                async cancellationToken => await global::Rask.Data.GeneratedModelWrites.Find<").Append(aggregate.FullyQualifiedName)
            .Append(">(id!, cancellationToken) is { } aggregate ? new ")
            .Append(input).Append("(id, ").Append(aggregate.Versioned ? "aggregate.Version" : "0").Append(')');
        var fills = operation.Parameters.Where(static p => p.Field!.Fill is not null)
            .Select(static p => p.Field!.Property + " = " + p.Field.Fill).ToList();
        if (fills.Count > 0)
        {
            s.Append(" { ").Append(string.Join(", ", fills)).Append(" }");
        }

        s.AppendLine(" : null,");
    }

    private static void AppendSubmit(StringBuilder s, OperatedAggregate aggregate, Operation operation)
    {
        var arguments = string.Join(", ", operation.Parameters.Select(static p => p.Name));
        s.Append("                async (input, cancellationToken) =>").AppendLine();
        s.AppendLine("                {");
        s.Append("                    var failures = new ").Append(FailureListFqn).AppendLine("();");
        s.Append("                    if (!").Append(operation.Name).Append("Read(input, failures").Append(Outs(operation)).AppendLine("))");
        s.AppendLine("                    {");
        s.AppendLine("                        throw new global::Rask.Cqrs.RaskValidationException(failures);");
        s.AppendLine("                    }");
        s.AppendLine();
        s.Append("                    await operation(");
        if (!operation.Factory)
        {
            s.Append("input.Id, ");
            if (aggregate.Versioned)
            {
                s.Append("input.Version, ");
            }
        }

        s.Append(arguments.Length > 0 ? arguments + ", " : "").AppendLine("cancellationToken);");
        s.AppendLine("                },");
    }

    private static string Outs(Operation operation) =>
        string.Concat(operation.Parameters.Select(static p => ", out var " + p.Name));

    // The fields made into the operation's arguments: each value object through its rules, every refusal collected.
    private static void AppendReader(StringBuilder s, OperatedAggregate aggregate, Operation operation)
    {
        var input = InputName(aggregate, operation);
        s.AppendLine();
        s.Append("    private static bool ").Append(operation.Name).Append("Read(").Append(input).Append(" input, ").Append(FailureListFqn).Append(" failures");
        foreach (var parameter in operation.Parameters)
        {
            s.Append(", out ").Append(parameter.Type).Append(' ').Append(parameter.Name);
        }

        s.AppendLine(")");
        s.AppendLine("    {");
        foreach (var parameter in operation.Parameters)
        {
            var field = parameter.Field!;
            s.Append("        ").Append(parameter.Name).Append(" = ").Append(Made(field)).AppendLine(";");
        }

        s.AppendLine("        return failures.Count == 0;");
        s.AppendLine("    }");
    }

    private static string Made(FormField field)
    {
        var given = "input." + field.Property;
        if (field.ValueObject is not { } valueObject)
        {
            return given;
        }

        var name = SymbolDisplay.FormatLiteral(field.Property, quote: true);
        string make;
        if (field.Optional)
        {
            make = valueObject + ".CreateOptional(" + given + ", " + name + ")";
        }
        else if (field.Text)
        {
            make = valueObject + ".Create(" + given + " ?? \"\", " + name + ")";
        }
        else
        {
            make = given + " is { } __given ? " + valueObject + ".Create(__given, " + name + ") : throw new global::Rask.Cqrs.RaskValidationException(new " +
                   FailureFqn + "[] { new " + FailureFqn + "(global::Rask.Rules.Required, new[] { " + name + " }) })";
        }

        return "global::Rask.Data.GeneratedOperations.Given(() => " + make + ", failures)" + (field.Optional ? "" : "!");
    }

    private static void AppendShapesAndRequired(StringBuilder s, Operation operation)
    {
        s.AppendLine();
        s.Append("    private static global::Rask.Core.Forms.FieldShape? ").Append(operation.Name).AppendLine("ShapeOf(string property) =>");
        s.AppendLine("        property switch");
        s.AppendLine("        {");
        foreach (var field in operation.Parameters.Select(static p => p.Field!).Where(static f => f.ValueObject is not null))
        {
            s.Append("            ").Append(SymbolDisplay.FormatLiteral(field.Property, quote: true)).Append(" => ").Append(field.ValueObject).AppendLine(".__Field,");
        }

        s.AppendLine("            _ => null,");
        s.AppendLine("        };");
        s.AppendLine();
        s.Append("    private static bool ").Append(operation.Name).AppendLine("Required(string property) =>");
        var required = operation.Parameters.Select(static p => p.Field!).Where(static f => f.Required).Select(static f => SymbolDisplay.FormatLiteral(f.Property, quote: true)).ToList();
        s.Append("        ").Append(required.Count == 0 ? "false" : "property is " + string.Join(" or ", required)).AppendLine(";");
    }

    // The operation's dry run, asked as the store's rules are: as each field is committed and once more on submit.
    private static void AppendRules(StringBuilder s, OperatedAggregate aggregate, Operation operation)
    {
        var input = InputName(aggregate, operation);
        var arguments = string.Join(", ", operation.Parameters.Select(static p => p.Name));
        s.AppendLine();
        s.Append("    private sealed class ").Append(operation.Name).AppendLine("Rules : global::Rask.Core.Forms.IStoreRules");
        s.AppendLine("    {");
        s.Append("        public async global::System.Threading.Tasks.ValueTask<global::System.Collections.Generic.IReadOnlyList<").Append(FailureFqn)
            .Append(">> Check(object model, string? field, ").Append(TokenFqn).AppendLine(" cancellationToken)");
        s.AppendLine("        {");
        s.Append("            var input = (").Append(input).AppendLine(")model;");
        s.Append("            var failures = new ").Append(FailureListFqn).AppendLine("();");
        s.Append("            if (!").Append(operation.Name).Append("Read(input, failures").Append(Outs(operation)).AppendLine("))");
        s.AppendLine("            {");
        s.AppendLine("                return failures;");
        s.AppendLine("            }");
        s.AppendLine();
        s.Append("            return await ").Append(aggregate.FullyQualifiedName).Append(".DryRun").Append(operation.Name).Append('(');
        if (!operation.Factory)
        {
            s.Append("input.Id, ");
            if (aggregate.Versioned)
            {
                s.Append("input.Version, ");
            }
        }

        s.Append(arguments.Length > 0 ? arguments + ", " : "").AppendLine("cancellationToken).ConfigureAwait(false);");
        s.AppendLine("        }");
        s.AppendLine("    }");
    }
}
