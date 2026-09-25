using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;

namespace Rask.Generators.ScopedScripts;

internal enum ReturnKind
{
    Void,
    Value,
    Object,

    /// <summary>A C# tuple, read element by element from the array the script returns.</summary>
    Tuple,
}

internal sealed class MappedParameter
{
    public string? Type { get; init; }

    /// <summary>A callback's async-handler form; null for any other parameter.</summary>
    public string? AsyncType { get; init; }

    public string? ArgFormat { get; init; }
    public bool Optional { get; init; }
    public string DefaultValue { get; init; } = " = null";
    public string? Error { get; init; }
    public List<string> Records { get; init; } = new();
}

internal sealed class MappedReturn
{
    public ReturnKind Kind { get; init; }
    public string? Type { get; init; }

    /// <summary>For <see cref="ReturnKind.Tuple" />: each element's C# type, in order.</summary>
    public List<string> Elements { get; init; } = new();
    public string? Error { get; init; }
    public List<string> Records { get; init; } = new();
}

internal sealed class RecordShape(string tsName, string csName, string? doc)
{
    public string TsName { get; } = tsName;
    public string CsName { get; } = csName;
    public string? Doc { get; } = doc;
    public List<(string CsName, string JsonName, string Type, bool Required)> Properties { get; } = new();
}

/// <summary>
///     TypeScript → C# for a scoped script's signatures. <c>number</c> is always <c>double</c>: TypeScript
///     has one numeric type, and a value that crosses as JSON keeps no trace of being whole.
/// </summary>
internal sealed class TypeMapper
{
    private const string JsonElement = "global::System.Text.Json.JsonElement";
    private const int MaxDepth = 16;

    private readonly TsDeclarations _decls;
    private readonly Dictionary<string, RecordShape?> _records = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _recordErrors = new(StringComparer.Ordinal);

    public TypeMapper(TsDeclarations decls)
    {
        _decls = decls;
        Classes = new HashSet<string>(decls.Classes.Select(c => c.Name), StringComparer.Ordinal);
    }

    /// <summary>The exported classes a signature may name — each has a nested proxy.</summary>
    public HashSet<string> Classes { get; }

    /// <summary>The records a mapped signature referenced, in first-use order.</summary>
    public IEnumerable<RecordShape> Records => _records.Values.Where(r => r is not null)!;

    public MappedParameter MapParameter(TsParam p, bool inProxy)
    {
        if (p.Rest)
        {
            return new MappedParameter { Error = "a rest parameter — take an array instead" };
        }

        var type = Unwrap(Resolve(p.Type), out var nullable);
        nullable |= p.Optional;
        var records = new List<string>();

        if (type is TsFunction fn)
        {
            return MapCallback(fn, nullable, p.Optional, inProxy);
        }

        if (type is TsNamed { Args.Count: 0 } named)
        {
            if (IsElement(named.Name))
            {
                return new MappedParameter { Type = "global::Rask.Core.ElementRef?", ArgFormat = "{0}", Optional = p.Optional };
            }

            if (IsAny(named.Name))
            {
                return new MappedParameter { Type = "object?", ArgFormat = "{0}", Optional = p.Optional };
            }

            if (Classes.Contains(named.Name))
            {
                var cs = Names.Pascal(named.Name)!;
                return new MappedParameter
                {
                    Type = nullable ? cs + "?" : cs,
                    ArgFormat = nullable ? "{0}?.Reference" : "{0}.Reference",
                    Optional = p.Optional,
                };
            }
        }

        if (type is TsTuple tuple)
        {
            if (nullable)
            {
                return new MappedParameter { Error = "a tuple that may be missing — make it required" };
            }

            var tupleType = MapTuple(tuple, records, out _, out var tupleError);
            if (tupleType is null)
            {
                return new MappedParameter { Error = tupleError };
            }

            // A C# tuple serializes as {} (its items are fields), so it crosses as the array the script expects.
            var items = string.Join(", ", tuple.Elements.Select((_, i) => "{0}.Item" + (i + 1)));
            return new MappedParameter
            {
                Type = tupleType,
                ArgFormat = "new object?[] {{ " + items + " }}",
                Records = records,
            };
        }

        var data = MapData(type, 0, records, out var error);
        if (data is null)
        {
            return new MappedParameter { Error = error };
        }

        return new MappedParameter
        {
            Type = Nullable(data, nullable),
            ArgFormat = "{0}",
            Optional = p.Optional,
            Records = records,
        };
    }

    public MappedReturn MapReturn(TsType returns)
    {
        var type = Resolve(returns);
        if (type is TsNamed { Name: "Promise", Args.Count: 1 } promise)
        {
            type = Resolve(promise.Args[0]);
        }

        type = Unwrap(type, out var nullable);
        if (type is TsNamed { Args.Count: 0 } named)
        {
            if (named.Name is "void" or "undefined" or "never")
            {
                return new MappedReturn { Kind = ReturnKind.Void };
            }

            if (IsAny(named.Name))
            {
                return new MappedReturn { Kind = ReturnKind.Value, Type = JsonElement };
            }

            if (IsElement(named.Name))
            {
                return new MappedReturn
                {
                    Error = $"an element ('{named.Name}'), which cannot come back to C# — return what you need from it instead (its size, its text)",
                };
            }

            if (Classes.Contains(named.Name))
            {
                return nullable
                    ? new MappedReturn { Error = $"'{named.Name} | null' — return the instance, or throw when there is none" }
                    : new MappedReturn { Kind = ReturnKind.Object, Type = Names.Pascal(named.Name) };
            }
        }

        if (type is TsFunction)
        {
            return new MappedReturn { Error = "a function, which cannot come back to C#" };
        }

        if (type is TsTuple tuple)
        {
            if (nullable)
            {
                return new MappedReturn { Error = "a tuple that may be missing — return the tuple, or throw when there is none" };
            }

            var tupleRecords = new List<string>();
            var tupleType = MapTuple(tuple, tupleRecords, out var elements, out var tupleError);
            return tupleType is null
                ? new MappedReturn { Error = tupleError }
                : new MappedReturn { Kind = ReturnKind.Tuple, Type = tupleType, Elements = elements, Records = tupleRecords };
        }

        var records = new List<string>();
        var data = MapData(type, 0, records, out var error);
        return data is null
            ? new MappedReturn { Error = error }
            : new MappedReturn { Kind = ReturnKind.Value, Type = Nullable(data, nullable), Records = records };
    }

    private MappedParameter MapCallback(TsFunction fn, bool nullable, bool optional, bool inProxy)
    {
        if (fn.Generic)
        {
            return new MappedParameter { Error = "a generic callback" };
        }

        if (fn.Parameters.Count > 2)
        {
            return new MappedParameter { Error = "a callback taking more than two arguments — pass one object instead" };
        }

        var returns = Unwrap(Resolve(fn.Returns), out _);
        if (returns is TsNamed { Name: "Promise", Args.Count: 1 } promise)
        {
            returns = Unwrap(Resolve(promise.Args[0]), out _);
        }

        if (returns is not TsNamed { Args.Count: 0, Name: "void" or "undefined" or "any" or "unknown" })
        {
            return new MappedParameter { Error = "a callback that returns a value to the script — a C# callback only runs" };
        }

        var records = new List<string>();
        var types = new List<string>();
        foreach (var arg in fn.Parameters)
        {
            if (arg.Rest)
            {
                return new MappedParameter { Error = "a callback with a rest parameter" };
            }

            var argType = Unwrap(Resolve(arg.Type), out var argNullable);
            string? mapped;
            if (argType is TsNamed { Args.Count: 0 } n && IsAny(n.Name))
            {
                mapped = JsonElement;
            }
            else
            {
                mapped = MapData(argType, 0, records, out var error);
                if (mapped is null)
                {
                    return new MappedParameter { Error = $"a callback whose argument '{arg.Name}' is {error}" };
                }

                mapped = Nullable(mapped, argNullable || arg.Optional);
            }

            types.Add(mapped);
        }

        // Taken as a delegate, in two overloads — a lambda has no conversion to the Callback struct, so a
        // Callback parameter would make the call site write `new(…)`. C# binds a sync lambda to the Action
        // form and an async one to the Task form, which is the same pair of shapes Callback itself takes.
        var list = string.Join(", ", types);
        var callback = types.Count == 0 ? "global::Rask.Core.Callback" : "global::Rask.Core.Callback<" + list + ">";
        var sync = types.Count == 0 ? "global::System.Action" : "global::System.Action<" + list + ">";
        var async = types.Count == 0
            ? "global::System.Func<global::System.Threading.Tasks.Task>"
            : "global::System.Func<" + list + ", global::System.Threading.Tasks.Task>";
        var make = (inProxy ? "ScriptCallback(" : "global::Rask.Core.ScopedAssets.ScopedScript.Callback(this, ")
                   + "new " + callback + "({0}))";
        return new MappedParameter
        {
            Type = nullable ? sync + "?" : sync,
            AsyncType = nullable ? async + "?" : async,
            ArgFormat = nullable ? "{0} is null ? null : " + make : make,
            Optional = optional,
            Records = records,
        };
    }

    /// <summary>A value that crosses as JSON: primitives, arrays, dictionaries, and the file's own shapes.</summary>
    private string? MapData(TsType type, int depth, List<string> records, out string? error)
    {
        error = null;
        if (depth > MaxDepth)
        {
            error = "nested too deeply";
            return null;
        }

        type = Unwrap(Resolve(type), out var nullable);
        string? mapped;
        switch (type)
        {
            case TsLiteral literal:
                mapped = literal.Kind switch
                {
                    TsLiteralKind.String => "string",
                    TsLiteralKind.Number => "double",
                    _ => "bool",
                };
                break;

            case TsArray array:
                mapped = MapData(array.Element, depth + 1, records, out error);
                mapped = mapped is null ? null : "global::System.Collections.Generic.IReadOnlyList<" + mapped + ">";
                break;

            case TsNamed { Name: "Array" or "ReadonlyArray", Args.Count: 1 } generic:
                mapped = MapData(generic.Args[0], depth + 1, records, out error);
                mapped = mapped is null ? null : "global::System.Collections.Generic.IReadOnlyList<" + mapped + ">";
                break;

            case TsNamed { Name: "Record", Args.Count: 2 } dict:
                if (Unwrap(Resolve(dict.Args[0]), out _) is not TsNamed { Name: "string" })
                {
                    error = "a Record keyed by something other than string";
                    return null;
                }

                mapped = MapData(dict.Args[1], depth + 1, records, out error);
                mapped = mapped is null
                    ? null
                    : "global::System.Collections.Generic.IReadOnlyDictionary<string, " + mapped + ">";
                break;

            case TsNamed { Args.Count: 0 } named:
                mapped = MapNamed(named.Name, depth, records, out error);
                break;

            case TsNamed named:
                error = $"'{named.Name}<…>', which has no C# counterpart here";
                return null;

            case TsUnion:
                error = "a union of different types — give it one type, or split the function";
                return null;

            case TsInlineObject:
                error = "an inline object type — declare it as an interface in the file so it has a name";
                return null;

            case TsObject:
                error = "an object type";
                return null;

            case TsFunction:
                error = "a function, which cannot travel inside data";
                return null;

            case TsTuple:
                error = "a tuple inside other data — a tuple crosses only as a whole parameter or return value; declare an interface instead";
                return null;

            case TsUnsupported unsupported:
                error = unsupported.Description;
                return null;

            default:
                error = "a type Rask does not map";
                return null;
        }

        return mapped is null ? null : Nullable(mapped, nullable);
    }

    // `[x: number, y: string]` → `(double X, string Y)`; unlabelled elements stay Item1, Item2.
    private string? MapTuple(TsTuple tuple, List<string> records, out List<string> elements, out string? error)
    {
        elements = new List<string>();
        var parts = new List<string>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in tuple.Elements)
        {
            var mapped = MapData(element.Type, 1, records, out error);
            if (mapped is null)
            {
                error = "a tuple whose element is " + error;
                return null;
            }

            elements.Add(mapped);
            var name = element.Label is { } label ? Names.Pascal(label) : null;
            parts.Add(name is not null && names.Add(name) && !name.StartsWith("Item", StringComparison.Ordinal)
                ? mapped + " " + name
                : mapped);
        }

        error = null;
        return "(" + string.Join(", ", parts) + ")";
    }

    private string? MapNamed(string name, int depth, List<string> records, out string? error)
    {
        error = null;
        switch (name)
        {
            case "string":
                return "string";
            case "number":
                return "double";
            case "boolean":
                return "bool";
            case "Date":
                return "global::System.DateTimeOffset";
        }

        if (IsAny(name))
        {
            return JsonElement;
        }

        if (name is "void" or "undefined" or "null" or "never")
        {
            error = $"'{name}'";
            return null;
        }

        if (_decls.Aliases.TryGetValue(name, out var alias) && alias is TsObject shape)
        {
            var record = MapRecord(name, shape, depth, out error);
            if (record is null)
            {
                return null;
            }

            records.Add(record.CsName);
            return record.CsName;
        }

        if (Classes.Contains(name))
        {
            error = $"an instance of '{name}', which travels only as a parameter or a return value, not inside data";
            return null;
        }

        if (IsElement(name))
        {
            error = $"an element ('{name}'), which cannot travel inside data";
            return null;
        }

        error = $"'{name}', which is not declared in this file — declare it as an interface here";
        return null;
    }

    private RecordShape? MapRecord(string name, TsObject shape, int depth, out string? error)
    {
        error = null;
        if (_recordErrors.TryGetValue(name, out var known))
        {
            error = known;
            return null;
        }

        if (_records.TryGetValue(name, out var existing))
        {
            // Null while this record is being mapped: a recursive shape refers to itself by name.
            return existing ?? new RecordShape(name, Names.Pascal(name) ?? name, null);
        }

        var csName = Names.Pascal(name);
        if (csName is null)
        {
            error = $"'{name}', whose name is not a C# identifier";
            _recordErrors[name] = error;
            return null;
        }

        _records[name] = null;
        var record = new RecordShape(name, csName, null);
        var nested = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in shape.Properties)
        {
            var propertyName = Names.Pascal(property.Name);
            if (propertyName is null || propertyName == csName || !seen.Add(propertyName))
            {
                error = $"'{name}', whose property '{property.Name}' has no usable C# name";
                break;
            }

            var type = MapData(property.Type, depth + 1, nested, out var propertyError);
            if (type is null)
            {
                error = $"'{name}', whose property '{property.Name}' is {propertyError}";
                break;
            }

            if (property.Optional && !type.EndsWith("?", StringComparison.Ordinal) && type != JsonElement)
            {
                type += "?";
            }

            record.Properties.Add((propertyName, property.Name, type, !property.Optional));
        }

        if (error is not null)
        {
            _records.Remove(name);
            _recordErrors[name] = error;
            return null;
        }

        _records[name] = record;
        return record;
    }

    // A type alias that is not an object shape is its target: `type Mode = "a" | "b"` is a string.
    private TsType Resolve(TsType type)
    {
        for (var i = 0; i < MaxDepth; i++)
        {
            if (type is TsNamed { Args.Count: 0 } named && _decls.Aliases.TryGetValue(named.Name, out var alias)
                && alias is not TsObject)
            {
                type = alias;
                continue;
            }

            break;
        }

        return type;
    }

    /// <summary>
    ///     Strips <c>null</c>/<c>undefined</c> from a union, and folds a union of literals of one kind — a
    ///     string enum — into that kind.
    /// </summary>
    private TsType Unwrap(TsType type, out bool nullable)
    {
        nullable = false;
        if (type is not TsUnion union)
        {
            return type;
        }

        var rest = new List<TsType>();
        foreach (var member in union.Members)
        {
            var resolved = Resolve(member);
            if (resolved is TsNamed { Name: "null" or "undefined", Args.Count: 0 })
            {
                nullable = true;
            }
            else
            {
                rest.Add(resolved);
            }
        }

        if (rest.Count == 0)
        {
            return new TsNamed("undefined");
        }

        if (rest.Count == 1)
        {
            return rest[0];
        }

        string? Kind(TsType t) => t switch
        {
            TsLiteral { Kind: TsLiteralKind.String } or TsNamed { Name: "string", Args.Count: 0 } => "string",
            TsLiteral { Kind: TsLiteralKind.Number } or TsNamed { Name: "number", Args.Count: 0 } => "number",
            TsLiteral { Kind: TsLiteralKind.Boolean } or TsNamed { Name: "boolean", Args.Count: 0 } => "boolean",
            _ => null,
        };

        var first = Kind(rest[0]);
        return first is not null && rest.All(r => Kind(r) == first) ? new TsNamed(first) : new TsUnion(rest);
    }

    private static string Nullable(string type, bool nullable) =>
        nullable && !type.EndsWith("?", StringComparison.Ordinal) && type != JsonElement ? type + "?" : type;

    private static bool IsAny(string name) => name is "any" or "unknown" or "object";

    private static bool IsElement(string name) =>
        name is "Element" or "Node" or "EventTarget"
        || (name.EndsWith("Element", StringComparison.Ordinal) && (name.StartsWith("HTML", StringComparison.Ordinal)
                                                                    || name.StartsWith("SVG", StringComparison.Ordinal)
                                                                    || name.StartsWith("MathML", StringComparison.Ordinal)));
}

internal static class Names
{
    /// <summary><c>width</c> → <c>Width</c>; null when the name has no C# spelling.</summary>
    public static string? Pascal(string name)
    {
        var sb = new StringBuilder(name.Length);
        var upper = true;
        foreach (var c in name)
        {
            if (c is '-' or ' ' or '.')
            {
                upper = true;
                continue;
            }

            if (!(char.IsLetterOrDigit(c) || c == '_'))
            {
                return null;
            }

            if (sb.Length == 0 && c == '_')
            {
                continue;
            }

            sb.Append(upper ? char.ToUpperInvariant(c) : c);
            upper = false;
        }

        return sb.Length == 0 || char.IsDigit(sb[0]) ? null : sb.ToString();
    }

    /// <summary>A TypeScript parameter name as a C# one — a keyword is escaped (<c>@event</c>).</summary>
    public static string Parameter(string name)
    {
        var clean = new string(name.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
        if (clean.Length == 0 || char.IsDigit(clean[0]))
        {
            clean = "_" + clean;
        }

        return SyntaxFacts.GetKeywordKind(clean) != SyntaxKind.None ? "@" + clean : clean;
    }
}
