using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Rask.Generators.Json;

namespace Rask.Core.Dom.Build;

// IDL types to C#: what crosses the wire as JSON, and the enums and records (IDL enums, dictionaries, and value objects
// that are only data, like DOMRect) an assembly has to declare for it. Core declares what element refs use; Rask.Web runs
// the element refs' pass first, marks those as Core's (MarkExternal), and declares only the rest, under `prefix`.
internal sealed class DomValueTypes(JsonNode root, string prefix = "")
{
    private const string CorePrefix = "global::Rask.Core.";

    private static readonly char[] Space = { ' ' };

    private readonly JsonNode _interfaces = root["interfaces"]!;
    private readonly JsonNode _enums = root["enums"]!;
    private readonly JsonNode _dictionaries = root["dictionaries"]!;
    private readonly SortedSet<string> _usedEnums = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, List<Field>> _records = new(StringComparer.Ordinal);

    // The dictionaries the browser answers with that hold an `any` (a stream read's value): generic on the caller's type.
    private readonly HashSet<string> _generic = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _recordParents = new(StringComparer.Ordinal);
    private readonly SortedSet<string> _serializable = new(StringComparer.Ordinal);
    private readonly HashSet<string> _visiting = new(StringComparer.Ordinal);
    private readonly HashSet<string> _handedOver = HandedOver(root);

    // The types another assembly (Core) declares: named, never declared again.
    private readonly HashSet<string> _external = new(StringComparer.Ordinal);

    // Everything mapped so far is Core's: Rask.Web calls this after running the element refs' pass.
    public void MarkExternal()
    {
        _external.UnionWith(_usedEnums);
        _external.UnionWith(_records.Keys);
        _serializable.Clear();
        _onlyData = true;
        _bytes = true;
        _unions = true;
    }

    // Core has no live objects, so there anything that serializes itself is a value; where live objects exist (Rask.Web),
    // only one that is nothing but data is.
    private bool _onlyData;

    // Bytes cross as base64 that Rask.Web's runtime turns back into a Uint8Array; an element ref's call has no such step,
    // so Core never maps them.
    private bool _bytes;

    // A union C# can still name in one type — a string or a number, a type or a list of it — and a record (a map) of
    // them. Rask.Web's converters carry the first; Core's element refs keep the surface they have.
    private bool _unions;

    // What C# hands over and reads back as byte[]: a buffer, or a view of one whose items are bytes.
    private static readonly HashSet<string> Bytes = new(StringComparer.Ordinal)
    {
        "ArrayBuffer", "SharedArrayBuffer", "DataView", "Int8Array", "Uint8Array", "Uint8ClampedArray",
        "BufferSource", "AllowSharedBufferSource", "ArrayBufferView",
    };

    // The views of wider numbers: still binary, so a union of them and bytes (BufferSource, spelled out) is bytes, but a
    // lone Float32Array is no byte[].
    private static readonly HashSet<string> WiderViews = new(StringComparer.Ordinal)
    {
        "Int16Array", "Uint16Array", "Int32Array", "Uint32Array", "Float16Array", "Float32Array", "Float64Array", "BigInt64Array",
        "BigUint64Array",
    };

    // Whether an IDL name crosses as a value (an enum, a dictionary, data that serializes itself or a callback is handed)
    // rather than a live object.
    public bool IsValue(string name) =>
        _external.Contains(name) || _enums[name] is not null || _dictionaries[name] is not null || (_interfaces[name] is not null && IsData(name));

    private bool IsData(string name) => ToJson(name) || _handedOver.Contains(name);

    // Whether an argument of this IDL type is an element on the page, which C# names by its ElementRef: Node, Element, or
    // an element's own interface (an observer's observe(target)).
    public bool IsElement(string idl)
    {
        var name = idl.TrimEnd('?');
        if (string.Equals(name, "Node", StringComparison.Ordinal))
        {
            return true;
        }

        for (var n = name; n is not null && _interfaces[n] is { } i; n = i["parent"]?.AsString())
        {
            if (string.Equals(n, "Element", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // A C# type the generator names itself (WebObjectArgs' keyframes), registered with the JSON context like any it mapped.
    public string Serializes(string type)
    {
        Serializable(type);
        return type;
    }

    // The type an element the browser names reads as: the app's ElementRef to it, which Rask.Web's runtime sends as the
    // element's data-rask-ref, or null.
    public string ElementRead()
    {
        _serializable.Add(CorePrefix + "ElementRef");
        return CorePrefix + "ElementRef?";
    }

    // Whether a C# type this mapped (bare or qualified) is one of MDN's enums: a value type, as a C# enum is.
    public bool IsEnum(string type) => _enums[type.Substring(type.LastIndexOf('.') + 1)] is not null;

    private string Qualify(string name) => _external.Contains(name) ? CorePrefix + name : prefix + name;

    public static void Header(StringBuilder sb)
    {
        sb.AppendLine("// <auto-generated/> from src/Rask.Core/Dom/mdn.snapshot.json by Rask.Dom.targets — do not edit.");
        sb.AppendLine("#nullable enable");
    }

    // The alternatives of a union type, `(boolean or ScrollIntoViewOptions)`, nested ones flattened (BufferSource spells
    // out as `((Int8Array or … or DataView) or ArrayBuffer)`); a plain type is its own.
    public static IEnumerable<string> Alternatives(string idl)
    {
        var nullable = idl.EndsWith("?", StringComparison.Ordinal) && idl.StartsWith("(", StringComparison.Ordinal);
        var inner = nullable ? idl.Substring(0, idl.Length - 1) : idl;
        if (!inner.StartsWith("(", StringComparison.Ordinal) || !inner.EndsWith(")", StringComparison.Ordinal))
        {
            return new[] { idl };
        }

        return TopLevel(inner.Substring(1, inner.Length - 2), " or ").SelectMany(Alternatives).Select(t => nullable ? t.TrimEnd('?') + "?" : t);
    }

    // `A or (B or C) or sequence<(D or E)>` → A, (B or C), sequence<(D or E)>: split at `separator` only outside brackets.
    private static List<string> TopLevel(string union, string separator)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < union.Length; i++)
        {
            if (union[i] is '(' or '<')
            {
                depth++;
            }
            else if (union[i] is ')' or '>')
            {
                depth--;
            }
            else if (depth == 0 && string.CompareOrdinal(union, i, separator, 0, separator.Length) == 0)
            {
                parts.Add(union.Substring(start, i - start));
                start = i + separator.Length;
            }
        }

        parts.Add(union.Substring(start));
        return parts;
    }

    // The C# type for an IDL type, or null when it does not cross the wire. `returned` allows `undefined` (void).
    public string? CSharp(string idl, bool returned)
    {
        var type = Map(idl, returned);
        if (type is not null && !string.Equals(type, "void", StringComparison.Ordinal))
        {
            Serializable(type);
        }

        return type;
    }

    private string? Map(string idl, bool returned)
    {
        if (idl.StartsWith("Promise<", StringComparison.Ordinal))
        {
            return returned ? Map(idl.Substring(8, idl.Length - 9), returned: true) : null;
        }

        var nullable = idl.EndsWith("?", StringComparison.Ordinal);
        var bare = nullable ? idl.Substring(0, idl.Length - 1) : idl;
        var suffix = nullable ? "?" : "";
        if (bare.StartsWith("(", StringComparison.Ordinal))
        {
            if (IsBytes(bare, returned))
            {
                return "byte[]" + suffix;
            }

            var union = _unions ? Union(bare, returned) : null;

            // A boolean-or-dictionary union is nullable already: its null is the `false`.
            return union is null || union.EndsWith("?", StringComparison.Ordinal) ? union : union + suffix;
        }

        if (_unions && bare.StartsWith("record<", StringComparison.Ordinal))
        {
            return MapOf(bare, returned) is { } map ? map + suffix : null;
        }

        if (_bytes && Bytes.Contains(bare))
        {
            return "byte[]" + suffix;
        }

        if (bare.StartsWith("sequence<", StringComparison.Ordinal) || bare.StartsWith("FrozenArray<", StringComparison.Ordinal))
        {
            var open = bare.IndexOf('<');
            var item = Map(bare.Substring(open + 1, bare.Length - open - 2), returned: false);
            return item is null || item.EndsWith("[]", StringComparison.Ordinal) ? null : item + "[]" + suffix;
        }

        if (string.Equals(bare, "undefined", StringComparison.Ordinal))
        {
            return returned ? "void" : null;
        }

        var primitive = Primitive(bare);
        if (primitive is not null)
        {
            return primitive + suffix;
        }

        var named = Named(bare, returned);
        var generic = named is not null && _generic.Contains(named) ? "<T>" : "";
        return named is null ? null : Qualify(named) + generic + suffix;
    }

    // A union C# names in one type. A type or a list of it is the list: a lone value is a list of one (a file picker's
    // accept, `".txt"` or `[".txt", ".text"]`). A string or a number C# hands over is a string, which StringOrNumber
    // writes as a number where it reads as one: the number is the short form the browser also takes as text (a
    // Bluetooth service, 0x180F or "battery_service"), where the text is a name (a mark's, "auto") it takes only as text.
    // A boolean or a dictionary (a media request's video, `true` or its constraints) is the dictionary, nullable: null
    // is `false`, not asked for, and an empty one is `true`, since any object is truthy. A media constraint (MDN's
    // ConstrainBoolean, ConstrainDouble, ConstrainULong, ConstrainDOMString) is its plain value, nullable: what a page
    // writes, `Width = 640`, which the browser takes as the ideal.
    private string? Union(string union, bool returned)
    {
        var alternatives = Alternatives(union).ToList();
        if (alternatives.Count == 2 && alternatives.FirstOrDefault(a => IsListOf(a, alternatives)) is { } list)
        {
            return Map(list, returned);
        }

        if (PlainConstraint(alternatives) is { } plain)
        {
            return plain + "?";
        }

        if (BooleanOrDictionary(alternatives) is { } dictionary)
        {
            return Map(dictionary, returned) is { } mapped ? mapped.TrimEnd('?') + "?" : null;
        }

        return !returned && IsStringOrNumber(alternatives) ? "string" : null;
    }

    // The dictionary of a `(boolean or Dictionary)` union, in either order; null for any other union.
    private string? BooleanOrDictionary(List<string> alternatives) =>
        alternatives.Count == 2 && alternatives.Any(a => string.Equals(a.TrimEnd('?'), "boolean", StringComparison.Ordinal))
            ? alternatives.Select(a => a.TrimEnd('?')).FirstOrDefault(a => _dictionaries[a] is not null)
            : null;

    // The C# primitive of a media constraint: one plain value (and perhaps a list of it) beside a Constrain… dictionary,
    // `(unsigned long or ConstrainULongRange)` → int. Null for any other union, one of two plain values among them.
    private string? PlainConstraint(List<string> alternatives)
    {
        var constraints = alternatives.Where(a => a.StartsWith("Constrain", StringComparison.Ordinal) && _dictionaries[a] is not null).ToList();
        var plain = alternatives.Except(constraints, StringComparer.Ordinal).ToList();
        var value = plain.Where(p => Primitive(p) is not null).ToList();
        return constraints.Count == 1 && value.Count == 1 && plain.All(p => string.Equals(p, value[0], StringComparison.Ordinal) || IsListOf(p, value))
            ? Primitive(value[0])
            : null;
    }

    private static bool IsListOf(string list, List<string> alternatives) =>
        Arrays.Any(a => alternatives.Any(item => string.Equals(list, a + item + ">", StringComparison.Ordinal)));

    internal static readonly string[] Arrays = { "sequence<", "FrozenArray<" };

    // A live alternative beside them (an animation's duration, `300` or a CSSNumericValue) is one C# never hands over.
    private bool IsStringOrNumber(List<string> alternatives)
    {
        var mapped = alternatives.Where(a => _interfaces[a] is null || IsValue(a)).Select(Primitive).ToList();
        return mapped.Contains("string") && mapped.Any(t => t is "int" or "long" or "double") && mapped.All(t => t is "string" or "int" or "long" or "double");
    }

    // Whether a field's IDL type is a string or a number, alone or in a list: what StringOrNumber carries.
    private bool IsStringOrNumber(string idl)
    {
        var bare = ItemOf(idl);
        return bare.StartsWith("(", StringComparison.Ordinal) && IsStringOrNumber(Alternatives(bare).ToList());
    }

    // `record<DOMString, V>`, a map from text, as a Dictionary.
    private string? MapOf(string record, bool returned)
    {
        var parts = TopLevel(record.Substring(7, record.Length - 8), ", ");
        var value = parts.Count == 2 && string.Equals(Primitive(parts[0]), "string", StringComparison.Ordinal) ? Map(parts[1], returned) : null;
        return value is null ? null : "global::System.Collections.Generic.Dictionary<string, " + value + ">";
    }

    // A union of buffers and views is bytes, as BufferSource is. One C# hands over that also takes a string is bytes too:
    // the string only spells the same bytes out (a push subscription's applicationServerKey, in base64url). One the
    // browser answers with is not, since it may well be the string (a FileReader's result).
    private bool IsBytes(string union, bool returned)
    {
        var alternatives = Alternatives(union).ToList();
        return _bytes && alternatives.Any(Bytes.Contains)
               && alternatives.All(t => Bytes.Contains(t) || WiderViews.Contains(t)
                                        || (!returned && string.Equals(Primitive(t), "string", StringComparison.Ordinal)));
    }

    private static string? Primitive(string idl) => idl switch
    {
        "DOMString" or "USVString" or "CSSOMString" or "ByteString" => "string",
        "boolean" => "bool",
        "byte" or "octet" or "short" or "unsigned short" or "long" or "unsigned long" => "int",
        "long long" or "unsigned long long" or "EpochTimeStamp" => "long",
        "float" or "unrestricted float" or "double" or "unrestricted double" or "DOMHighResTimeStamp" => "double",
        _ => null,
    };

    // An IDL enum, dictionary, or value object with toJSON: declared once, as a C# enum or record.
    // `returned`: the browser answers with it. A dictionary it answers with that holds an `any` is generic, T for the
    // caller's own type: `ReadableStreamReadResult<T>`, read as `reader.Read<byte[]>()`.
    private string? Named(string name, bool returned = false)
    {
        if (_enums[name] is not null)
        {
            _usedEnums.Add(name);
            return name;
        }

        var dictionary = _dictionaries[name];
        var generic = _unions && returned && dictionary is not null && dictionary["parent"] is null
                      && (dictionary["members"]?.Items ?? new List<JsonNode>()).Any(f => string.Equals(f["type"]?.AsString(), "any", StringComparison.Ordinal));
        if (_records.ContainsKey(name) || _visiting.Contains(name))
        {
            return _generic.Contains(name) == generic
                ? name
                : throw new DomEmitException($"{name} is both handed over and answered with: decide which of the two its `any` is.");
        }

        var source = dictionary ?? (_interfaces[name] is { } value && IsData(name) ? value : null);
        if (source is null)
        {
            return null;
        }

        _visiting.Add(name);
        var parent = source["parent"]?.AsString();
        var parentType = parent is null ? null : Named(parent);
        var fields = Fields(source, dictionary is null, parentType, generic);
        _visiting.Remove(name);
        if (fields.Count == 0 && parentType is null)
        {
            return null;
        }

        if (generic)
        {
            _generic.Add(name);
        }

        _records[name] = fields;
        _recordParents[name] = parentType;
        return name;
    }

    // One field of a record: its property, C# type and JSON name, whether MDN requires it, and the converter that
    // writes it where the context's own would not (StringOrNumber's).
    private sealed class Field(string name, string type, string json, bool required, string? converter)
    {
        public string Json => json;

        public void Deconstruct(out string Name, out string Type, out string Json, out bool Required, out string? Converter) =>
            (Name, Type, Json, Required, Converter) = (name, type, json, required, converter);
    }

    private const string StringOrNumber = "global::Rask.Web.StringOrNumberJsonConverter";
    private const string StringsOrNumbers = "global::Rask.Web.StringsOrNumbersJsonConverter";

    // The converter a field's union needs, if any: a string or a number (StringOrNumber); a value or a list of it, which
    // the browser may answer with as the lone value it was set to (OneOrMany, an ICE server's urls); a boolean or a
    // dictionary, which the browser may answer with as the boolean (BooleanOrDictionary: `true` is an empty one); a
    // media constraint, which it may answer with as the dictionary (PlainConstraint: its ideal, else its exact).
    private string? Converter(string idl, string type)
    {
        if (IsStringOrNumber(idl))
        {
            return type.EndsWith("[]", StringComparison.Ordinal) ? StringsOrNumbers : StringOrNumber;
        }

        var bare = idl.TrimEnd('?');
        var alternatives = bare.StartsWith("(", StringComparison.Ordinal) ? Alternatives(bare).ToList() : new List<string>();
        if (PlainConstraint(alternatives) is not null)
        {
            // typeof(…<string?>) is not C#: a reference type is named bare, a value type as its Nullable.
            return "global::Rask.Web.PlainConstraintJsonConverter<" + (IsValueType(type.TrimEnd('?')) ? type : type.TrimEnd('?')) + ">";
        }

        if (BooleanOrDictionary(alternatives) is not null)
        {
            return "global::Rask.Web.BooleanOrDictionaryJsonConverter<" + type.TrimEnd('?') + ">";
        }

        return alternatives.Count == 2 && alternatives.Any(a => IsListOf(a, alternatives))
            ? "global::Rask.Web.OneOrManyJsonConverter<" + type.TrimEnd('?').Substring(0, type.TrimEnd('?').Length - 2) + ">"
            : null;
    }

    private List<Field> Fields(JsonNode source, bool valueObject, string? parentType, bool generic)
    {
        var inherited = new HashSet<string>(StringComparer.Ordinal);
        for (var p = parentType; p is not null && _records.TryGetValue(p, out var pf); p = _recordParents[p])
        {
            inherited.UnionWith(pf.Select(f => f.Json));
        }

        var fields = new List<Field>();
        foreach (var f in source["members"]?.Items ?? new List<JsonNode>())
        {
            if (valueObject && !string.Equals(f["kind"]?.AsString(), "attribute", StringComparison.Ordinal))
            {
                continue;
            }

            var json = f["name"]!.AsString()!;
            var idl = f["type"]!.AsString()!;
            var type = generic && string.Equals(idl, "any", StringComparison.Ordinal) ? "T" : Map(idl, returned: false);
            if (type is not null && inherited.Add(json))
            {
                Serializable(type);
                var converter = Converter(idl, type);
                fields.Add(new Field(DomRefEmitter.Pascal(json), type, json, f["required"]?.AsBoolean() == true, converter));
            }
        }

        return fields;
    }

    // A value object serializes itself (toJSON). Where live objects exist it must also do nothing else: DOMRect is data,
    // PushSubscription, which can unsubscribe, is a live object.
    internal bool ToJson(string name)
    {
        var json = false;
        var more = false;
        for (var n = name; n is not null && _interfaces[n] is { } i; n = i["parent"]?.AsString())
        {
            foreach (var m in (i["members"]?.Items ?? new List<JsonNode>()).Where(m => string.Equals(m["kind"]?.AsString(), "operation", StringComparison.Ordinal)))
            {
                var isToJson = string.Equals(m["name"]?.AsString(), "toJSON", StringComparison.Ordinal);
                json |= isToJson;
                more |= !isToJson;
            }
        }

        // …nor may it be a class with behaviour of its own: URL has URL.canParse and new URL(…).
        var behaviour = _interfaces[name]?["constructors"] is not null || _interfaces[name]?["statics"] is not null;
        return json && !(_onlyData && (more || behaviour));
    }

    // What a callback is handed that is only data — every member a read-only attribute, no events, nothing static
    // (IntersectionObserverEntry, MutationRecord, a Lock) — and the same among its fields' types (ResizeObserverSize).
    // It is read once, when the callback runs, so it crosses as a record, like DOMRect, wherever it appears.
    private static HashSet<string> HandedOver(JsonNode root)
    {
        var interfaces = root["interfaces"]!;
        var pending = new Queue<string>();
        foreach (var callback in root["callbacks"]?.Members ?? new List<KeyValuePair<string, JsonNode>>())
        {
            foreach (var a in callback.Value["args"]?.Items ?? new List<JsonNode>())
            {
                pending.Enqueue(a["type"]!.AsString()!);
            }
        }

        // …and what a read-only list attribute holds, or an event's, where it is nothing but values (a gamepad's buttons,
        // a USB device's configurations, a motion event's acceleration): read whole, as records. One that holds a live
        // object too (an XR input source's spaces) stays live, since its record would lose it.
        foreach (var i in interfaces.Members)
        {
            var isEvent = Derives(interfaces, i.Key, "Event");
            foreach (var m in (i.Value["members"]?.Items ?? new List<JsonNode>()).Where(m => m["readonly"]?.AsBoolean() == true))
            {
                var type = m["type"]!.AsString()!;
                if ((isEvent || Arrays.Any(a => type.StartsWith(a, StringComparison.Ordinal))) && AllValues(root, ItemOf(type), new HashSet<string>(StringComparer.Ordinal)))
                {
                    pending.Enqueue(type);
                }
            }
        }

        var result = new HashSet<string>(StringComparer.Ordinal);
        while (pending.Count > 0)
        {
            var name = ItemOf(pending.Dequeue());
            if (result.Contains(name) || interfaces[name] is null || !OnlyData(interfaces, name))
            {
                continue;
            }

            result.Add(name);
            for (var n = name; n is not null && interfaces[n] is { } i; n = i["parent"]?.AsString())
            {
                foreach (var m in i["members"]?.Items ?? new List<JsonNode>())
                {
                    pending.Enqueue(m["type"]!.AsString()!);
                }
            }
        }

        return result;
    }

    // `sequence<MutationRecord>?` → MutationRecord.
    internal static string ItemOf(string idl)
    {
        var bare = idl.TrimEnd('?');
        var open = bare.IndexOf('<');
        return open > 0 && bare.EndsWith(">", StringComparison.Ordinal) ? ItemOf(bare.Substring(open + 1, bare.Length - open - 2)) : bare;
    }

    internal static bool Derives(JsonNode interfaces, string name, string ancestor)
    {
        for (var n = name; n is not null; n = interfaces[n]?["parent"]?.AsString())
        {
            if (string.Equals(n, ancestor, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // Whether an interface is only data all the way down: every attribute a primitive, an enum, a dictionary, bytes, or
    // an interface that is the same.
    private static bool AllValues(JsonNode root, string name, HashSet<string> visiting)
    {
        var interfaces = root["interfaces"]!;
        if (visiting.Contains(name))
        {
            return true;
        }

        if (interfaces[name] is null || !OnlyData(interfaces, name))
        {
            return false;
        }

        visiting.Add(name);
        for (var n = name; n is not null && interfaces[n] is { } i; n = i["parent"]?.AsString())
        {
            foreach (var m in i["members"]?.Items ?? new List<JsonNode>())
            {
                var item = ItemOf(m["type"]!.AsString()!);
                var value = Primitive(item) is not null || Bytes.Contains(item) || root["enums"]![item] is not null || root["dictionaries"]![item] is not null;
                if (!value && !AllValues(root, item, visiting))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool OnlyData(JsonNode interfaces, string name)
    {
        for (var n = name; n is not null && interfaces[n] is { } i; n = i["parent"]?.AsString())
        {
            var behaviour = (i["members"]?.Items ?? new List<JsonNode>())
                .Any(m => !string.Equals(m["kind"]?.AsString(), "attribute", StringComparison.Ordinal) || m["readonly"]?.AsBoolean() != true);
            if (behaviour || i["events"] is not null || i["statics"] is not null)
            {
                return false;
            }
        }

        return true;
    }

    private bool IsValueType(string type) => type is "bool" or "int" or "long" or "double" || IsEnum(type);

    // typeof(string?) is not C#: a reference type is registered bare, a value type both ways.
    private void Serializable(string type)
    {
        // The caller's own type, or a record of it, is the host's to read (as any InvokeAsync<T>), not this context's.
        var bare = type.TrimEnd('?');
        if (bare is "T" || bare.EndsWith("<T>", StringComparison.Ordinal))
        {
            return;
        }

        _serializable.Add(bare);
        var name = bare.Substring(bare.LastIndexOf('.') + 1);
        if (bare is "bool" or "int" or "long" or "double" || _usedEnums.Contains(name))
        {
            _serializable.Add(bare + "?");
        }
    }

    // This assembly's enums and records, in `ns`, and a JSON context `context` for every type its members send or read,
    // which writes byte arrays with `bytes`: a converter that marks them for the browser to turn back into bytes.
    public string Declarations(string ns, string context, string? bytes = null)
    {
        var sb = new StringBuilder();
        Header(sb);
        sb.AppendLine("using System.Text.Json.Serialization;");
        sb.AppendLine();
        sb.Append("namespace ").Append(ns).AppendLine(";");
        sb.AppendLine();
        foreach (var name in _usedEnums.Where(n => !_external.Contains(n)))
        {
            Enum(sb, name);
        }

        foreach (var record in _records.Where(r => !_external.Contains(r.Key)))
        {
            Record(sb, record.Key, record.Value);
        }

        foreach (var t in _serializable)
        {
            sb.Append("[JsonSerializable(typeof(").Append(t).AppendLine("))]");
        }

        if (bytes is not null && _serializable.Contains("byte[]"))
        {
            sb.Append("[JsonSourceGenerationOptions(Converters = new[] { typeof(").Append(bytes).AppendLine(") })]");
        }

        sb.Append("internal sealed partial class ").Append(context).AppendLine(" : JsonSerializerContext;");
        return sb.ToString();
    }

    private void Enum(StringBuilder sb, string name)
    {
        sb.Append("/// <summary>MDN's <c>").Append(name).AppendLine("</c> enumeration, as it crosses to the browser.</summary>");
        sb.Append("[JsonConverter(typeof(JsonStringEnumConverter<").Append(name).AppendLine(">))]");
        sb.Append("public enum ").AppendLine(name);
        sb.AppendLine("{");
        foreach (var v in _enums[name]!.Items.Select(v => v.AsString()!))
        {
            sb.Append("    /// <summary>The value <c>\"").Append(v).AppendLine("\"</c>.</summary>");
            sb.Append("    [JsonStringEnumMemberName(\"").Append(v).AppendLine("\")]");
            sb.Append("    ").Append(EnumMember(v)).AppendLine(",");
            sb.AppendLine();
        }

        sb.AppendLine("}");
        sb.AppendLine();
    }

    // "smooth" → Smooth, "2d-array" → _2dArray (an identifier cannot start with a digit), "" → Empty.
    private static string EnumMember(string value)
    {
        if (value.Length == 0)
        {
            return "Empty";
        }

        var words = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            words.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }

        var name = string.Concat(words.ToString().Split(Space, StringSplitOptions.RemoveEmptyEntries).Select(DomRefEmitter.Pascal));
        return char.IsDigit(name[0]) ? "_" + name : name;
    }

    private void Record(StringBuilder sb, string name, List<Field> fields)
    {
        // Open to any dictionary MDN derives from it, which Rask.Web may declare beside it.
        var isBase = _dictionaries.Members.Any(d => string.Equals(d.Value["parent"]?.AsString(), name, StringComparison.Ordinal))
                     || _interfaces.Members.Any(i => string.Equals(i.Value["parent"]?.AsString(), name, StringComparison.Ordinal));
        var generic = _generic.Contains(name);
        sb.Append("/// <summary>MDN's <c>").Append(name).AppendLine("</c>, as it crosses to and from the browser.</summary>");
        if (generic)
        {
            sb.AppendLine("/// <typeparam name=\"T\">Your own type, what its <c>any</c> holds: <c>byte[]</c> for bytes.</typeparam>");
        }

        sb.Append("public ").Append(isBase ? "" : "sealed ").Append("record ").Append(name).Append(generic ? "<T>" : "");
        if (_recordParents[name] is { } parent)
        {
            sb.Append(" : ").Append(Qualify(parent));
        }

        sb.AppendLine();
        sb.AppendLine("{");

        // A dictionary is filled in by C#, so what it does not require is optional. An interface's data is the browser's,
        // with every field it has, nullable exactly where MDN says: `rect.Width`, `entry.IsIntersecting`. So is a generic
        // one, which only the browser answers with: `result.Done`.
        var browsers = _dictionaries[name] is null || generic;
        foreach (var (property, type, json, required, converter) in fields)
        {
            sb.Append("    /// <summary>MDN's <c>").Append(json).AppendLine("</c>.</summary>");
            sb.Append("    [JsonPropertyName(\"").Append(json).AppendLine("\")]");
            if (converter is not null)
            {
                sb.Append("    [JsonConverter(typeof(").Append(converter).AppendLine("))]");
            }

            var nullable = type.EndsWith("?", StringComparison.Ordinal);
            if (!required && (!browsers || nullable))
            {
                sb.AppendLine("    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]");
            }

            var declared = required || browsers || nullable ? type : type + "?";
            sb.Append("    public ").Append(required ? "required " : "").Append(declared).Append(' ').Append(property).Append(" { get; init; }");
            sb.AppendLine(browsers && !nullable && !IsValueType(type) ? " = default!;" : "");
            sb.AppendLine();
        }

        sb.AppendLine("}");
        sb.AppendLine();
    }
}
