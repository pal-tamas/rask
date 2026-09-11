using System.Collections.Generic;
using System.Globalization;
using Rask.Generators.Json;

namespace Rask.Generators.External.PackageIslands;

/// <summary>Reads a committed <c>{Island}.props.json</c> into a <see cref="PropsSnapshot" />.</summary>
/// <remarks>
///     <para>
///         Tolerant where tolerance is safe and strict where it is not. An unknown field is ignored, so an
///         extractor that learns to write something new does not break a build that predates it. An
///         unknown <em>kind</em> is kept as written, so the resolver can say which prop it could not
///         generate. A schema newer than this reader is a defect: a major version exists precisely to
///         mark a change an older reader would misread.
///     </para>
///     <para>
///         Never throws — see <see cref="JsonLite" />.
///     </para>
/// </remarks>
internal static class PropsSnapshotReader
{
    /// <summary>The newest snapshot schema this reader understands.</summary>
    public const int MaxSchema = 1;

    private static readonly SnapshotType Unknown = new(
        "?", false, default, false, null, default, default, null, default, false);

    public static PropsSnapshot Read(string path, string text)
    {
        var parsed = JsonLite.Parse(text);
        if (parsed.Root is null)
        {
            return Defective(path, parsed.Defect ?? "the file is empty", parsed.Line, parsed.Column);
        }

        var root = parsed.Root;
        if (root.Kind != JsonKind.Object)
        {
            return Defective(path, "a props snapshot must be a JSON object", root.Line, root.Column);
        }

        if (root["schema"] is not { } schemaNode)
        {
            return Defective(path, "it has no 'schema' version", root.Line, root.Column);
        }

        if (!TryReadSchema(schemaNode, out var schema) || schema < 1)
        {
            return Defective(path, "its 'schema' is not a version this reader recognises", schemaNode.Line, schemaNode.Column);
        }

        if (schema > MaxSchema)
        {
            return Defective(
                path,
                $"it was written by a newer extractor (schema {schema}), and this Rask.External reads schema {MaxSchema}",
                schemaNode.Line,
                schemaNode.Column);
        }

        var runtime = root["runtime"]?.AsString();
        var module = root["module"]?.AsString();
        if (runtime is null || module is null)
        {
            return Defective(path, "it does not say which 'runtime' and 'module' it describes", root.Line, root.Column);
        }

        if (root["props"] is not { Kind: JsonKind.Array } propsNode)
        {
            return Defective(path, "it has no 'props' array", root.Line, root.Column);
        }

        var props = new List<SnapshotProp>();
        foreach (var item in propsNode.Items)
        {
            if (item.Kind != JsonKind.Object || item["name"]?.AsString() is not { Length: > 0 } name)
            {
                return Defective(path, "a prop without a 'name'", item.Line, item.Column);
            }

            props.Add(new SnapshotProp(
                name,
                item["wire"]?.AsString() ?? name,
                item["required"]?.AsBoolean() ?? false,
                item["doc"]?.AsString(),
                Raw(item["default"]),
                ReadType(item["type"], 0),
                item.Line,
                item.Column));
        }

        var skipped = new List<SnapshotSkip>();
        if (root["skipped"] is { Kind: JsonKind.Array } skippedNode)
        {
            foreach (var item in skippedNode.Items)
            {
                if (item["name"]?.AsString() is { } name)
                {
                    skipped.Add(new SnapshotSkip(
                        name,
                        item["reason"]?.AsString() ?? "unsupported",
                        item["detail"]?.AsString()));
                }
            }
        }

        var types = new List<SnapshotNamedType>();
        if (root["types"] is { Kind: JsonKind.Object } typesNode)
        {
            foreach (var member in typesNode.Members)
            {
                types.Add(new SnapshotNamedType(member.Key, ReadType(member.Value, 0)));
            }
        }

        var package = root["package"];

        return new PropsSnapshot(
            path,
            schema,
            runtime,
            module,
            root["export"]?.AsString() ?? "default",
            package?["name"]?.AsString(),
            package?["version"]?.AsString(),
            root["tag"]?.AsString(),
            root["content"]?.AsString() ?? "none",
            new EquatableArray<SnapshotProp>(props),
            new EquatableArray<SnapshotSkip>(skipped),
            new EquatableArray<SnapshotNamedType>(types),
            null,
            0,
            0);
    }

    private static bool TryReadSchema(JsonNode node, out int schema)
    {
        schema = 0;
        if (node.TryGetNumber(out var number))
        {
            schema = (int)number;
            return number >= 1 && number == System.Math.Floor(number) && number < int.MaxValue;
        }

        // "1.3" reads as major 1: a minor version is additive by definition.
        if (node.AsString() is { } text)
        {
            var major = text.Split('.')[0];
            return int.TryParse(major, NumberStyles.None, CultureInfo.InvariantCulture, out schema);
        }

        return false;
    }

    private static SnapshotType ReadType(JsonNode? node, int depth)
    {
        // The document itself is already depth-capped by JsonLite, so this can only recurse as deep as
        // the JSON is nested — but a type tree is walked again downstream, and a fixed ceiling here keeps
        // every later walk trivially bounded too.
        if (node is not { Kind: JsonKind.Object } || depth > 32)
        {
            return Unknown;
        }

        var kind = node["kind"]?.AsString() ?? "?";
        var nullable = node["nullable"]?.AsBoolean() ?? false;

        var values = new List<SnapshotLiteral>();
        if (node["values"] is { Kind: JsonKind.Array } valuesNode)
        {
            foreach (var value in valuesNode.Items)
            {
                switch (value.Kind)
                {
                    case JsonKind.String:
                        values.Add(new SnapshotLiteral(value.Text, false, false));
                        break;
                    case JsonKind.Number:
                        values.Add(new SnapshotLiteral(value.Text, true, false));
                        break;
                    case JsonKind.True:
                        values.Add(new SnapshotLiteral("true", false, true));
                        break;
                    case JsonKind.False:
                        values.Add(new SnapshotLiteral("false", false, true));
                        break;
                }
            }
        }

        var element = node["element"] ?? node["value"];
        var of = new List<SnapshotType>();
        if (node["of"] is { } ofNode)
        {
            if (ofNode.Kind == JsonKind.Array)
            {
                foreach (var alternative in ofNode.Items)
                {
                    of.Add(ReadType(alternative, depth + 1));
                }
            }
            else
            {
                element ??= ofNode;
            }
        }

        var members = new List<SnapshotMember>();
        if (node["members"] is { Kind: JsonKind.Array } membersNode)
        {
            foreach (var member in membersNode.Items)
            {
                if (member["name"]?.AsString() is { } name)
                {
                    members.Add(new SnapshotMember(
                        name,
                        member["required"]?.AsBoolean() ?? false,
                        member["doc"]?.AsString(),
                        ReadType(member["type"], depth + 1)));
                }
            }
        }

        var args = new List<SnapshotArg>();
        if (node["args"] is { Kind: JsonKind.Array } argsNode)
        {
            for (var i = 0; i < argsNode.Items.Count; i++)
            {
                var arg = argsNode.Items[i];

                // Both spellings: a parameter object, or a bare type when the extractor had no name.
                var typeNode = arg["type"] is { Kind: JsonKind.Object } typed ? typed : arg;
                args.Add(new SnapshotArg(
                    arg["name"]?.AsString() ?? "arg" + i.ToString(CultureInfo.InvariantCulture),
                    arg["optional"]?.AsBoolean() ?? false,
                    ReadType(typeNode, depth + 1)));
            }
        }

        var returns = node["returns"] switch
        {
            null => false,
            { Kind: JsonKind.False } => false,
            { Kind: JsonKind.Null } => false,
            { Kind: JsonKind.String } r => !string.Equals(r.Text, "void", System.StringComparison.Ordinal),
            _ => true,
        };

        return new SnapshotType(
            kind,
            nullable,
            new EquatableArray<SnapshotLiteral>(values),
            node["open"]?.AsBoolean() ?? false,
            element is null ? null : ReadType(element, depth + 1),
            new EquatableArray<SnapshotType>(of),
            new EquatableArray<SnapshotMember>(members),
            node["name"]?.AsString(),
            new EquatableArray<SnapshotArg>(args),
            returns);
    }

    // A default is documentation, so any JSON value is accepted and kept as the text a reader would see.
    private static string? Raw(JsonNode? node) => node?.Kind switch
    {
        null => null,
        JsonKind.String or JsonKind.Number => node.Text,
        JsonKind.True => "true",
        JsonKind.False => "false",
        _ => null,
    };

    private static PropsSnapshot Defective(string path, string defect, int line, int column) => new(
        path,
        0,
        string.Empty,
        string.Empty,
        "default",
        null,
        null,
        null,
        "none",
        default,
        default,
        default,
        defect,
        line <= 0 ? 1 : line,
        column <= 0 ? 1 : column);
}
