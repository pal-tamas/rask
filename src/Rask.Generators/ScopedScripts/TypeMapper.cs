using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;

namespace Rask.Generators.ScopedScripts;

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

        if (type is TsFunction fn)
        {
            return MapCallback(fn, nullable, p.Optional, inProxy);
        }

        if (type is TsNamed { Args.Count: 0 } named && MapReferenceParameter(named.Name, nullable, p.Optional) is { } reference)
        {
            return reference;
        }

        if (type is TsTuple tuple)
        {
            return MapTupleParameter(tuple, nullable);
        }

        var records = new List<string>();
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

    // A parameter that crosses by reference rather than as JSON: an element, anything, or an exported class's proxy.
    private MappedParameter? MapReferenceParameter(string name, bool nullable, bool optional)
    {
        if (IsElement(name))
        {
            return new MappedParameter { Type = "global::Rask.Core.ElementRef?", ArgFormat = "{0}", Optional = optional };
        }

        if (IsAny(name))
        {
            return new MappedParameter { Type = "object?", ArgFormat = "{0}", Optional = optional };
        }

        if (Classes.Contains(name))
        {
            var cs = Names.Pascal(name)!;
            return new MappedParameter
            {
                Type = nullable ? cs + "?" : cs,
                ArgFormat = nullable ? "{0}?.Reference" : "{0}.Reference",
                Optional = optional,
            };
        }

        return null;
    }

    private MappedParameter MapTupleParameter(TsTuple tuple, bool nullable)
    {
        if (nullable)
        {
            return new MappedParameter { Error = "a tuple that may be missing — make it required" };
        }

        var records = new List<string>();
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

    public MappedReturn MapReturn(ITsType returns)
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
        if (MapCallbackArguments(fn, records, types) is { } argumentError)
        {
            return new MappedParameter { Error = argumentError };
        }

        // Taken as a delegate, in two overloads — a lambda has no conversion to the Callback struct, so a
        // Callback parameter would make the call site write `new(…)`. C# binds a sync lambda to the Action
        // form and an async one to the Task form, which is the same pair of shapes Callback itself takes.
        var list = string.Join(", ", types);
        var callback = types.Count == 0 ? "global::Rask.Core.Callback" : "global::Rask.Core.Callback<" + list + ">";
        var syncHandler = types.Count == 0 ? "global::System.Action" : "global::System.Action<" + list + ">";
        var asyncHandler = types.Count == 0
            ? "global::System.Func<global::System.Threading.Tasks.Task>"
            : "global::System.Func<" + list + ", global::System.Threading.Tasks.Task>";
        var make = (inProxy ? "ScriptCallback(" : "global::Rask.Core.ScopedAssets.ScopedScript.Callback(this, ")
                   + "new " + callback + "({0}))";
        return new MappedParameter
        {
            Type = nullable ? syncHandler + "?" : syncHandler,
            AsyncType = nullable ? asyncHandler + "?" : asyncHandler,
            ArgFormat = nullable ? "{0} is null ? null : " + make : make,
            Optional = optional,
            Records = records,
        };
    }

    // Each argument's C# type into `types`; the error for the first that cannot cross, or null.
    private string? MapCallbackArguments(TsFunction fn, List<string> records, List<string> types)
    {
        foreach (var arg in fn.Parameters)
        {
            if (arg.Rest)
            {
                return "a callback with a rest parameter";
            }

            var argType = Unwrap(Resolve(arg.Type), out var argNullable);
            if (argType is TsNamed { Args.Count: 0 } n && IsAny(n.Name))
            {
                types.Add(JsonElement);
                continue;
            }

            var mapped = MapData(argType, 0, records, out var error);
            if (mapped is null)
            {
                return $"a callback whose argument '{arg.Name}' is {error}";
            }

            types.Add(Nullable(mapped, argNullable || arg.Optional));
        }

        return null;
    }

    /// <summary>A value that crosses as JSON: primitives, arrays, dictionaries, and the file's own shapes.</summary>
    private string? MapData(ITsType type, int depth, List<string> records, out string? error)
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
                mapped = MapList(array.Element, depth, records, out error);
                break;

            case TsNamed { Name: "Array" or "ReadonlyArray", Args.Count: 1 } generic:
                mapped = MapList(generic.Args[0], depth, records, out error);
                break;

            case TsNamed { Name: "Record", Args.Count: 2 } dict:
                mapped = MapDictionary(dict, depth, records, out error);
                break;

            case TsNamed { Args.Count: 0 } named:
                mapped = MapNamed(named.Name, depth, records, out error);
                break;

            default:
                error = Unmappable(type);
                return null;
        }

        return mapped is null ? null : Nullable(mapped, nullable);
    }

    private string? MapList(ITsType element, int depth, List<string> records, out string? error)
    {
        var mapped = MapData(element, depth + 1, records, out error);
        return mapped is null ? null : "global::System.Collections.Generic.IReadOnlyList<" + mapped + ">";
    }

    private string? MapDictionary(TsNamed dict, int depth, List<string> records, out string? error)
    {
        if (Unwrap(Resolve(dict.Args[0]), out _) is not TsNamed { Name: "string" })
        {
            error = "a Record keyed by something other than string";
            return null;
        }

        var mapped = MapData(dict.Args[1], depth + 1, records, out error);
        return mapped is null
            ? null
            : "global::System.Collections.Generic.IReadOnlyDictionary<string, " + mapped + ">";
    }

    // Why a type that is not data cannot cross as JSON.
    private static string Unmappable(ITsType type) => type switch
    {
        TsNamed named => $"'{named.Name}<…>', which has no C# counterpart here",
        TsUnion => "a union of different types — give it one type, or split the function",
        TsInlineObject => "an inline object type — declare it as an interface in the file so it has a name",
        TsObject => "an object type",
        TsFunction => "a function, which cannot travel inside data",
        TsTuple =>
            "a tuple inside other data — a tuple crosses only as a whole parameter or return value; declare an interface instead",
        TsUnsupported unsupported => unsupported.Description,
        _ => "a type Rask does not map",
    };

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
                error = $"a tuple whose element is {error}";
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

        _records.Add(name, null);
        var record = new RecordShape(name, csName, null);
        error = MapProperties(shape, depth, record);
        if (error is not null)
        {
            _records.Remove(name);
            _recordErrors[name] = error;
            return null;
        }

#pragma warning disable S4143 // replaces the null placeholder that kept first-use order and marked the recursion
        _records[name] = record;
#pragma warning restore S4143
        return record;
    }

    // Each of the shape's properties onto the record; the error for the first that cannot cross, or null.
    private string? MapProperties(TsObject shape, int depth, RecordShape record)
    {
        var name = record.TsName;
        var nested = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in shape.Properties)
        {
            var propertyName = Names.Pascal(property.Name);
            if (propertyName is null || string.Equals(propertyName, record.CsName, StringComparison.Ordinal)
                || !seen.Add(propertyName))
            {
                return $"'{name}', whose property '{property.Name}' has no usable C# name";
            }

            var type = MapData(property.Type, depth + 1, nested, out var propertyError);
            if (type is null)
            {
                return $"'{name}', whose property '{property.Name}' is {propertyError}";
            }

            if (property.Optional && !type.EndsWith("?", StringComparison.Ordinal)
                && !string.Equals(type, JsonElement, StringComparison.Ordinal))
            {
                type += "?";
            }

            record.Properties.Add((propertyName, property.Name, type, !property.Optional));
        }

        return null;
    }

    // A type alias that is not an object shape is its target: `type Mode = "a" | "b"` is a string.
    private ITsType Resolve(ITsType type)
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
    private ITsType Unwrap(ITsType type, out bool nullable)
    {
        nullable = false;
        if (type is not TsUnion union)
        {
            return type;
        }

        var rest = new List<ITsType>();
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

        string? Kind(ITsType t) => t switch
        {
            TsLiteral { Kind: TsLiteralKind.String } or TsNamed { Name: "string", Args.Count: 0 } => "string",
            TsLiteral { Kind: TsLiteralKind.Number } or TsNamed { Name: "number", Args.Count: 0 } => "number",
            TsLiteral { Kind: TsLiteralKind.Boolean } or TsNamed { Name: "boolean", Args.Count: 0 } => "boolean",
            _ => null,
        };

        var first = Kind(rest[0]);
        return first is not null && rest.All(r => string.Equals(Kind(r), first, StringComparison.Ordinal)) ? new TsNamed(first) : new TsUnion(rest);
    }

    private static string Nullable(string type, bool nullable) =>
        nullable && !type.EndsWith("?", StringComparison.Ordinal) && !string.Equals(type, JsonElement, StringComparison.Ordinal)
            ? type + "?"
            : type;

    private static bool IsAny(string name) => name is "any" or "unknown" or "object";

    private static bool IsElement(string name) =>
        name is "Element" or "Node" or "EventTarget"
        || (name.EndsWith("Element", StringComparison.Ordinal) && (name.StartsWith("HTML", StringComparison.Ordinal)
                                                                    || name.StartsWith("SVG", StringComparison.Ordinal)
                                                                    || name.StartsWith("MathML", StringComparison.Ordinal)));
}
