using System.Text.Json.Nodes;

namespace Rask.Flux;

/// <summary>
///     One measured example, as the page's collect() wrote it: its section and ordinal, its size and where it was, its
///     nodes, and the states forced on its controls. A view over the JSON, which stays what is written to disk.
/// </summary>
public sealed class Example
{
    private Dictionary<string, MeasuredNode>? byId;
    private ILookup<string, MeasuredNode>? children;
    private HashSet<string>? measured;

    /// <summary>A view over <paramref name="json" />.</summary>
    public Example(JsonObject json)
    {
        Json = json;
        Nodes = (json["nodes"] as JsonArray ?? []).Select(node => new MeasuredNode((JsonObject)node!)).ToList();
    }

    /// <summary>The JSON behind the view.</summary>
    public JsonObject Json { get; }

    /// <summary>The example's nodes, in document order, the wrapper first.</summary>
    public IReadOnlyList<MeasuredNode> Nodes { get; private set; }

    /// <summary>The section it was measured under: the &lt;h2 id&gt; above it, or the wrapper's data-section.</summary>
    public string Section => Js.Str(Json["section"]) ?? "";

    /// <summary>Its place among the examples of its section, or null where none was recorded.</summary>
    public int? Ordinal => Json["ordinal"] is { } o ? (int)Js.Number(o) : null;

    /// <summary>Where the example was in the viewport as it was measured.</summary>
    public double[] Origin => (Json["origin"] as JsonArray ?? []).Select(Js.Number).ToArray();

    /// <summary>Its width and height.</summary>
    public double[] Size => (Json["size"] as JsonArray ?? []).Select(Js.Number).ToArray();

    /// <summary>The forced states recorded: { node, state, changed }.</summary>
    public IEnumerable<JsonObject> States => (Json["states"] as JsonArray ?? []).OfType<JsonObject>();

    /// <summary>The node of that id.</summary>
    public MeasuredNode? Node(string? id) => id is not null && (byId ??= Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal)).TryGetValue(id, out var node) ? node : null;

    /// <summary>The parent of <paramref name="node" />.</summary>
    public MeasuredNode? ParentOf(MeasuredNode node) => Node(node.Parent);

    /// <summary>The children of <paramref name="node" />, in order.</summary>
    public IEnumerable<MeasuredNode> ChildrenOf(MeasuredNode node) =>
        (children ??= Nodes.Where(n => n.Parent is not null).ToLookup(n => n.Parent!, StringComparer.Ordinal))[node.Id];

    /// <summary><paramref name="node" /> and every node above it, nearest first.</summary>
    public IEnumerable<MeasuredNode> WithAncestors(MeasuredNode node)
    {
        for (var n = node; n is not null; n = ParentOf(n))
        {
            yield return n;
        }
    }

    /// <summary>Whether the node and every node above it is displayed.</summary>
    public bool Shown(MeasuredNode node) => WithAncestors(node).All(n => n.Style("display") != "none");

    /// <summary>What a forced <paramref name="state" /> changed on the node, or an empty object.</summary>
    public JsonObject Changed(MeasuredNode node, string state) =>
        States.FirstOrDefault(s => Js.Str(s["node"]) == node.Id && Js.Str(s["state"]) == state)?["changed"] as JsonObject ?? [];

    /// <summary>The ids whose states were measured (<see cref="Measure.StateTargets" />), worked out once.</summary>
    public bool Measured(MeasuredNode node, bool layout) => (measured ??= [.. Measure.StateTargets(Nodes, layout)]).Contains(node.Id);

    /// <summary>Keeps only the nodes <paramref name="keep" /> keeps — in the view and in the JSON.</summary>
    public void Filter(Func<MeasuredNode, bool> keep)
    {
        var kept = Nodes.Where(keep).ToList();
        Json["nodes"] = new JsonArray([.. kept.Select(n => n.Json.DeepClone())]);
        Nodes = (Json["nodes"] as JsonArray)!.Select(node => new MeasuredNode((JsonObject)node!)).ToList();
        byId = null;
        children = null;
        measured = null;
    }

    /// <summary>The examples of one scheme of a measurement.</summary>
    public static List<Example> Of(JsonNode? scheme) => (scheme as JsonArray ?? []).OfType<JsonObject>().Select(json => new Example(json)).ToList();
}

/// <summary>One measured element: its tag, attributes, own text, box, computed style, pseudo-elements and animations.</summary>
public sealed class MeasuredNode(JsonObject json)
{
    /// <summary>The JSON behind the view.</summary>
    public JsonObject Json { get; } = json;

    /// <summary><c>{index}-{i}</c>.</summary>
    public string Id { get; } = Js.Str(json["id"]) ?? "";

    /// <summary>The parent's id; null for the root.</summary>
    public string? Parent { get; } = Js.Str(json["parent"]);

    /// <summary>The lower-case tag name.</summary>
    public string Tag { get; } = Js.Str(json["tag"]) ?? "";

    /// <summary>Its own text, trimmed and joined, up to 80 characters.</summary>
    public string Text { get; } = Js.Str(json["text"]) ?? "";

    /// <summary>The attributes kept (no class, style or framework directive).</summary>
    public JsonObject Attrs => Json["attrs"] as JsonObject ?? [];

    /// <summary>x, y (from the example's origin), width, height.</summary>
    public double[] Box { get; } = (json["box"] as JsonArray ?? []).Select(Js.Number).ToArray();

    /// <summary>The computed styles.</summary>
    public JsonObject Styles => Json["style"] as JsonObject ?? [];

    /// <summary>One computed style; null where none was recorded.</summary>
    public string? Style(string key) => Js.Str(Styles[key]);

    /// <summary><c>"true"</c>, <c>"held"</c> or <c>"false"</c>: whether it, or what it is in, can be pressed.</summary>
    public string Pressable => Json["pressable"] is JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.String ? v.GetValue<string>() : Js.Truthy(Json["pressable"]) ? "true" : "false";

    /// <summary>A layout's node that is not rendered.</summary>
    public bool Hidden => Js.Truthy(Json["hidden"]);

    /// <summary>Whether the attribute is on it.</summary>
    public bool Has(string attribute) => Attrs.ContainsKey(attribute);

    /// <summary>An attribute's value, or null.</summary>
    public string? Attr(string attribute) => Js.Str(Attrs[attribute]);

    /// <summary>The <c>::before</c> or <c>::after</c> pseudo-element, or null.</summary>
    public JsonObject? Pseudo(string pseudo) => Json[pseudo] as JsonObject;

    /// <summary>The animations recorded on it, or null.</summary>
    public JsonNode? Animations => Json["animations"];
}
