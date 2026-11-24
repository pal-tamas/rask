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

    internal static readonly DiagnosticDescriptor Rask188 = new(
        "RASK188",
        "An operation gets no Form.For, as one of its parameters is no field a form holds",
        "'{0}.{1}' gets no 'Form.For({0}.{1}, …)': {2}",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Info,
        true,
        description: "Form.For(Supply.Change, id) is a form whose fields are the operation's parameters: a value (text, "
                     + "number, day, bool, enum, id), a value object of one value, a value object of several values, a list "
                     + "of rows. An operation that takes anything else — or more than 13 parameters, which no entry delegate "
                     + "holds — gets no form, and this says which parameter keeps it from one.",
        helpLinkUri: DiagnosticHelp.Link("RASK188"));

    /// <summary>One parameter as a field of the operation's form, or null for a type no form field holds.</summary>
    /// <param name="Property">The field's name: the parameter's, capitalised, as the aggregate names what it sets.</param>
    /// <param name="PropertyType">What the field holds: the parameter's own type, or a value object's one value.</param>
    /// <param name="ValueObject">The value object the field is made into, or null for a value taken as it is.</param>
    /// <param name="Optional">Whether the parameter may be null.</param>
    /// <param name="Required">Whether the field must be filled.</param>
    /// <param name="Text">Whether the value object holds a text.</param>
    /// <param name="Fill">What the aggregate holds for it, read off <c>aggregate</c>, or null when it names nothing alike.</param>
    private sealed record FormField(
        string Property, string PropertyType, string? ValueObject, bool Optional, bool Required, bool Text, string? Fill)
    {
        /// <summary>The <c>Change&lt;T&gt;</c> the parameter is, or null for a value taken as it is.</summary>
        public string? Change { get; init; }

        /// <summary>Whether its value may be taken away: a "Clear" beside the field (<c>{Property}Cleared</c>).</summary>
        public bool Clearable { get; init; }

        /// <summary>The C# of what a control bound to it is told (<c>DriverName.__Field</c>), or null for nothing.</summary>
        public string? Shape { get; init; }

        /// <summary>Whether a value type is held as its nullable twin, and so refused as required when left empty.</summary>
        public bool Lifted { get; init; }

        /// <summary>The C# a field nothing fills starts from: the parameter's declared default, or null for none.</summary>
        public string? Default { get; init; }

        /// <summary>The fields of a value object of several values, or null for a field of one value.</summary>
        public FormGroup? Group { get; init; }

        /// <summary>The rows of a list, or null for a field of one value.</summary>
        public FormRows? Rows { get; init; }
    }

    private static FormField? FieldOf(IParameterSymbol parameter, INamedTypeSymbol aggregate)
    {
        var input = InputName(aggregate.Name, parameter.ContainingSymbol.Name);
        return ParameterFieldOf(parameter, aggregate) ?? GroupFieldOf(parameter, aggregate, input) ?? RowsFieldOf(parameter, aggregate, input);
    }

    private static FormField? ParameterFieldOf(IParameterSymbol parameter, INamedTypeSymbol aggregate)
    {
        // Change<T>: an empty field keeps the value, so nothing is read into it; a nullable T may be cleared too.
        if (parameter.Type is INamedTypeSymbol { Name: "Change", TypeArguments.Length: 1, ContainingNamespace: { Name: "Values", ContainingNamespace.Name: "Rask" } } change)
        {
            var held = change.TypeArguments[0];
            var clearable = held.NullableAnnotation == NullableAnnotation.Annotated ||
                            held.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
            var bare = held is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
                ? nullable.TypeArguments[0]
                : held.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
            return ValueFieldOf(parameter, bare, aggregate) is { } field
                ? field with
                {
                    PropertyType = Optionally(field.PropertyType),
                    Optional = true,
                    Required = false,
                    Lifted = false,
                    Fill = null,
                    Change = change.ToDisplayString(TypeFormat),
                    Clearable = clearable,
                }
                : null;
        }

        return ValueFieldOf(parameter, parameter.Type, aggregate);
    }

    private static string Optionally(string type) => type.EndsWith("?", StringComparison.Ordinal) ? type : type + "?";

    private static FormField? ValueFieldOf(IParameterSymbol parameter, ITypeSymbol parameterType, INamedTypeSymbol aggregate)
    {
        var property = Capitalised(parameter.Name);
        var optional = parameter.NullableAnnotation == NullableAnnotation.Annotated ||
                       parameterType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
        var held = aggregate.GetMembers(property).OfType<IPropertySymbol>().FirstOrDefault(static p => !p.IsStatic);
        // A declared default (`int count = 3`) is what the field starts from where nothing is read into it; a null one is empty.
        var field = MemberField(property, parameterType, optional, held, "aggregate", lift: true);
        return field is { ValueObject: null } && parameter is { HasExplicitDefaultValue: true, ExplicitDefaultValue: not null }
            ? field with { Default = DefaultOf(parameter) }
            : field;
    }

    // The one value a value object holds: the positional parameter of one declared from Text, Number, Whole, Day or
    // Moment, or of a one-member `record TankSize(int Value) : Value` — whose value is one a field holds as it is.
    private static ITypeSymbol? OneValueOf(INamedTypeSymbol type) =>
        AggregateShape.DeclaredValueBaseOf(type) switch
        {
            "Text" or "Number" or "Whole" or "Day" or "Moment" => PositionalValueOf(type),
            "Value" when AggregateShape.HoldsOneValue(type) && PositionalValueOf(type) is { } held && IsFormValue(held) => held,
            _ => null,
        };

    private static ITypeSymbol? PositionalValueOf(INamedTypeSymbol type) =>
        type.InstanceConstructors
            .Where(c => c.Parameters.Length == 1 && !SymbolEqualityComparer.Default.Equals(c.Parameters[0].Type, type))
            .Select(static c => c.Parameters[0].Type)
            .FirstOrDefault();

    // Only the bases a control is drawn for declare what it is told (`__Field`); a `: Value` one declares nothing.
    private static bool DeclaresShape(INamedTypeSymbol type) =>
        AggregateShape.DeclaredValueBaseOf(type) is "Text" or "Number" or "Whole" or "Day" or "Moment";

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

    private static string? RenderForms(
        OperatedAggregate aggregate, Dictionary<string, int> signatures, List<string> ambiguous, List<Unformed> unformed)
    {
        var formed = Formed(aggregate, signatures, ambiguous, unformed);
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

        AppendShapeHelpers(s, formed);
        s.AppendLine("}");
        return s.ToString();
    }

    // The operations a form can be told apart for: every parameter one a field holds, and no other entry alike.
    private static List<(Operation Operation, string Entry)> Formed(
        OperatedAggregate aggregate, Dictionary<string, int> signatures, List<string> ambiguous, List<Unformed> unformed)
    {
        var formed = new List<(Operation Operation, string Entry)>();
        foreach (var operation in aggregate.Operations)
        {
            if (EntryDelegate(aggregate, operation) is not { } entry)
            {
                unformed.Add(new Unformed(aggregate.Name, operation.Name, WhyUnformed(operation), operation.Location));
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

    // What keeps an operation from a form, for RASK188: the first parameter no field holds, or too many of them.
    private static string WhyUnformed(Operation operation) =>
        operation.Parameters.FirstOrDefault(static p => p.Field is null) is { } parameter
            ? "its parameter '" + parameter.Name.TrimStart('@') + "' (" + parameter.Type.Replace("global::", "") +
              ") is no value, value object or list of rows a field holds — take a value object of it, or a value"
            : "it takes " + operation.Parameters.Count + " parameters, and a form's entry holds at most " + MostFormParameters +
              " — gather some into a value object of several values";

    /// <summary>An operation no <c>Form.For</c> is generated for, and why (RASK188).</summary>
    private sealed record Unformed(string Aggregate, string Operation, string Why, SymbolLocation? Location);

    // The helpers a shape above is written with, once each: a range's end, a value object's lengths its attributes tighten.
    private static void AppendShapeHelpers(StringBuilder s, List<(Operation Operation, string Entry)> formed)
    {
        var shapes = formed
            .SelectMany(static f => f.Operation.Parameters.Select(static p => p.Field!))
            .SelectMany(static f => (f.Group?.Members ?? f.Rows?.Members ?? new EquatableArray<FormField>([])).Append(f))
            .SelectMany(static f => (f.Group?.Members ?? new EquatableArray<FormField>([])).Append(f))
            .Select(static f => f.Shape)
            .OfType<string>()
            .ToList();
        if (shapes.Exists(static shape => shape.StartsWith(EndOf, StringComparison.Ordinal)))
        {
            AppendEndOf(s);
        }

        if (FieldShapeHints.UseWithin(shapes))
        {
            FieldShapeHints.AppendWithin(s);
        }
    }

    private static void AppendRegistrations(StringBuilder s, OperatedAggregate aggregate, List<(Operation Operation, string Entry)> formed)
    {
        s.AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]");
        foreach (var (_, type) in formed.SelectMany(f => NestedModels(aggregate, f.Operation)))
        {
            s.Append("    [global::System.Diagnostics.CodeAnalysis.DynamicDependency(global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties, typeof(")
                .Append(type).AppendLine("))]");
        }

        s.AppendLine("    internal static void Register()");
        s.AppendLine("    {");
        foreach (var (operation, _) in formed)
        {
            var input = InputName(aggregate, operation);
            s.Append("        ").Append(ValidationFqn).Append(".RegisterFieldShapes(typeof(").Append(input).Append("), ").Append(operation.Name).AppendLine("ShapeOf);");
            s.Append("        ").Append(ValidationFqn).Append(".RegisterRequiredFields(typeof(").Append(input).Append("), ").Append(operation.Name).AppendLine("Required);");
            foreach (var (key, type) in NestedModels(aggregate, operation))
            {
                s.Append("        ").Append(ValidationFqn).Append(".RegisterFieldShapes(typeof(").Append(type).Append("), ").Append(key).AppendLine("ShapeOf);");
                s.Append("        ").Append(ValidationFqn).Append(".RegisterRequiredFields(typeof(").Append(type).Append("), ").Append(key).AppendLine("Required);");
            }

            // A value object of several values that may be absent: nothing in its group is required until one is filled.
            var optional = operation.Parameters.Select(static p => p.Field!).Where(static f => f is { Group: not null, Optional: true }).ToList();
            if (optional.Count > 0)
            {
                s.Append("        ").Append(ValidationFqn).Append(".RegisterOptionalFields(typeof(").Append(input).Append("), static property => property is ")
                    .Append(string.Join(" or ", optional.Select(static f => SymbolDisplay.FormatLiteral(f.Property, quote: true)))).AppendLine(");");
            }

            foreach (var rows in operation.Parameters.Select(static p => p.Field!.Rows).OfType<FormRows>())
            {
                var optionalGroups = rows.Members.Where(static m => m is { Group: not null, Optional: true }).ToList();
                if (optionalGroups.Count > 0)
                {
                    s.Append("        ").Append(ValidationFqn).Append(".RegisterOptionalFields(typeof(").Append(input).Append('.').Append(rows.Model)
                        .Append("), static property => property is ")
                        .Append(string.Join(" or ", optionalGroups.Select(static f => SymbolDisplay.FormatLiteral(f.Property, quote: true)))).AppendLine(");");
                }
            }

            s.Append("        ").Append(ValidationFqn).Append(".RegisterStoreRules(typeof(").Append(input).Append("), static _ => new ").Append(operation.Name).AppendLine("Rules());");
            var clearable = operation.Parameters.Select(static p => p.Field!).Where(static f => f.Clearable).ToList();
            if (clearable.Count > 0)
            {
                s.Append("        ").Append(ValidationFqn).Append(".RegisterClearing(typeof(").Append(input).Append("), static property => property switch { ")
                    .Append(string.Join(", ", clearable.Select(static f => SymbolDisplay.FormatLiteral(f.Property, quote: true) + " => " + SymbolDisplay.FormatLiteral(f.Property + "Cleared", quote: true))))
                    .AppendLine(", _ => null });");
            }
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

    private static string InputName(OperatedAggregate aggregate, Operation operation) => InputName(aggregate.Name, operation.Name);

    private static string InputName(string aggregate, string operation) => aggregate + operation + "Input";

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
            AppendProperty(s, "    ", field, "What <c>" + operation.Name + "</c> is given as <c>" + parameter.Name.TrimStart('@') + "</c>.");
            if (field.Clearable)
            {
                s.Append("    /// <summary>Whether <c>").Append(field.Property).AppendLine("</c> is to be cleared rather than kept: the Clear beside its field.</summary>");
                s.Append("    public bool ").Append(field.Property).AppendLine("Cleared { get; set; }");
                s.AppendLine();
            }
        }

        foreach (var field in operation.Parameters.Select(static p => p.Field!))
        {
            if (field.Group is { } group)
            {
                AppendNested(s, group.Model, "The fields of <c>" + field.Property + "</c>: one per member of the value object.", group.Members);
            }
            else if (field.Rows is { } rows)
            {
                AppendNested(s, rows.Model, "One row of <c>" + field.Property + "</c>: add one with <c>" + field.Property + ".Add(new())</c>.", rows.Members);
                foreach (var (_, member) in RowGroups(operation.Name, field))
                {
                    AppendNested(s, member.Group!.Model, "The fields of a row's <c>" + member.Property + "</c>: one per member of the value object.", member.Group.Members);
                }
            }
        }

        s.AppendLine("}");
        s.AppendLine();
    }

    private static void AppendProperty(StringBuilder s, string indent, FormField field, string summary)
    {
        s.Append(indent).Append("/// <summary>").Append(summary).AppendLine("</summary>");
        s.Append(indent).Append("public ").Append(field.PropertyType).Append(' ').Append(field.Property).Append(" { get; set; }");
        if (field.Default is { } start)
        {
            s.Append(" = ").Append(start).Append(';');
        }
        else if (field.Group is not null)
        {
            s.Append(" = new();");
        }
        else if (field.Rows is not null)
        {
            s.Append(" = [];");
        }
        else if (field.Text && !field.PropertyType.EndsWith("?", StringComparison.Ordinal))
        {
            s.Append(" = \"\";");
        }

        s.AppendLine();
        s.AppendLine();
    }

    private static void AppendNested(StringBuilder s, string model, string summary, EquatableArray<FormField> members)
    {
        s.Append("    /// <summary>").Append(summary).AppendLine("</summary>");
        s.Append("    public sealed class ").AppendLine(model);
        s.AppendLine("    {");
        foreach (var member in members)
        {
            AppendProperty(s, "        ", member, "Its <c>" + member.Property + "</c>.");
        }

        s.AppendLine("    }");
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
            s.Append("        ").Append(parameter.Name).Append(" = ").Append(Made(operation, field)).AppendLine(";");
        }

        s.AppendLine("        return failures.Count == 0;");
        s.AppendLine("    }");

        foreach (var field in operation.Parameters.Select(static p => p.Field!))
        {
            if (field.Group is not null)
            {
                AppendGroupMade(s, input, operation, field);
            }
            else if (field.Rows is not null)
            {
                AppendRowsMade(s, input, operation, field);
            }
        }
    }

    private static string Made(Operation operation, FormField field)
    {
        var name = SymbolDisplay.FormatLiteral(field.Property, quote: true);
        var given = "input." + field.Property;
        if (field.Group is not null)
        {
            return operation.Name + field.Property + "Made(" + given + ", " + name + ", failures)" + (field.Optional ? "" : "!");
        }

        if (field.Rows is not null)
        {
            return "[.. " + operation.Name + field.Property + "Made(" + given + ", " + name + ", failures)]";
        }

        return field.Change is { } change ? ChangeMade(field, change) : MadeFrom(field, given, name);
    }

    // An empty field keeps; a ticked Clear clears; a value typed in is set, made through its value object's rules.
    private static string ChangeMade(FormField field, string change)
    {
        var given = "input." + field.Property;
        var name = SymbolDisplay.FormatLiteral(field.Property, quote: true);
        var rest = field.Clearable
            ? "input." + field.Property + "Cleared ? " + change + ".Clear : " + change + ".Keep"
            : change + ".Keep";
        var value = field.Text ? given + "!" : "__" + field.Property;
        var made = field.ValueObject is { } valueObject
            ? "global::Rask.Data.GeneratedOperations.Given(() => " + valueObject + ".Create(" + value + ", " + name + "), failures)!"
            : value;
        var filled = field.Text ? "!string.IsNullOrWhiteSpace(" + given + ")" : given + " is { } __" + field.Property;
        return filled + " ? " + change + ".Set(" + made + ") : " + rest;
    }

    // Per model — the form's own, and each group's and each row's — what each field is told and which must be filled.
    private static void AppendShapesAndRequired(StringBuilder s, Operation operation)
    {
        var fields = operation.Parameters.Select(static p => p.Field!).ToList();
        AppendShapesAndRequired(s, operation.Name, fields);
        foreach (var field in fields)
        {
            if ((field.Group?.Members ?? field.Rows?.Members) is { } members)
            {
                AppendShapesAndRequired(s, operation.Name + field.Property, [.. members]);
            }

            foreach (var (key, member) in RowGroups(operation.Name, field))
            {
                AppendShapesAndRequired(s, key, [.. member.Group!.Members]);
            }
        }
    }

    private static void AppendShapesAndRequired(StringBuilder s, string key, List<FormField> fields)
    {
        s.AppendLine();
        s.Append("    private static ").Append(FieldShapeFqn).Append("? ").Append(key).AppendLine("ShapeOf(string property) =>");
        s.AppendLine("        property switch");
        s.AppendLine("        {");
        foreach (var field in fields.Where(static f => f.Shape is not null))
        {
            s.Append("            ").Append(SymbolDisplay.FormatLiteral(field.Property, quote: true)).Append(" => ").Append(field.Shape).AppendLine(",");
        }

        s.AppendLine("            _ => null,");
        s.AppendLine("        };");
        s.AppendLine();
        s.Append("    private static bool ").Append(key).AppendLine("Required(string property) =>");
        var required = fields.Where(static f => f.Required).Select(static f => SymbolDisplay.FormatLiteral(f.Property, quote: true)).ToList();
        s.Append("        ").Append(required.Count == 0 ? "false" : "property is " + string.Join(" or ", required)).AppendLine(";");
    }

    // Each nested model's type, as `typeof(…)`; the form walks their properties to name a row's field (`Parts[1].Quantity`).
    private static IEnumerable<(string Key, string Type)> NestedModels(OperatedAggregate aggregate, Operation operation)
    {
        var input = InputName(aggregate, operation);
        foreach (var field in operation.Parameters.Select(static p => p.Field!))
        {
            if ((field.Group?.Model ?? field.Rows?.Model) is { } model)
            {
                yield return (operation.Name + field.Property, input + "." + model);
            }

            foreach (var (key, member) in RowGroups(operation.Name, field))
            {
                yield return (key, input + "." + member.Group!.Model);
            }
        }
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
