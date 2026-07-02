using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;

namespace Rask.Core;

/// <summary>
///     A stable handle to a rendered DOM element, for handing that element to JavaScript
///     (third-party widgets — charts, datepickers, editors — that need the raw node). Rask's
///     analogue of Blazor's <c>ElementReference</c>.
/// </summary>
/// <remarks>
///     <para>
///         Create one (typically in a field so its id is stable across renders) and attach it to
///         an element via the universal <c>Ref:</c> parameter, which stamps
///         <c>data-rask-ref="{id}"</c> onto the element:
///         <code>
///         private readonly ElementRef _chart = ElementRef.New();
///         protected override Component? Render() => Canvas(Ref: _chart);
///         </code>
///     </para>
///     <para>
///         Pass it as an argument to <see cref="IJSRuntime" /> and the client resolves it to the
///         live element before calling your function — no marker-class convention needed:
///         <code>
///         await js.InvokeVoidAsync("Rask.MyChart.init", _chart, data);  // JS receives the element
///         await _chart.FocusAsync(js);                                   // built-in helper
///         </code>
///     </para>
///     <para>
///         Wire format: an <see cref="ElementRef" /> serializes to <c>{"__raskRef__":"id"}</c>;
///         the runtime's JSON reviver swaps that for <c>document.querySelector('[data-rask-ref="id"]')</c>.
///         Ids are GUID hex (from <see cref="New" />), so they are always selector-safe.
///     </para>
/// </remarks>
// A reference type (not a struct): the only allocation is on ElementRef.New() — once per ref'd
// element, typically a field initializer — so element refs cost nothing on the render hot path.
// (A struct here would force every element factory to carry a struct optional parameter, which
// measurably regressed the counter-render allocation pin; a nullable-reference param is free.)
[JsonConverter(typeof(ElementRefJsonConverter))]
public sealed class ElementRef
{
    internal const string Marker = "__raskRef__";

    internal ElementRef(string id) => Id = id;

    /// <summary>The opaque element id, emitted as <c>data-rask-ref</c> and matched client-side.</summary>
    public string Id { get; }

    /// <summary>Mint a new ref with a unique, selector-safe id. Store it in a field for stability.</summary>
    public static ElementRef New() => new(Guid.NewGuid().ToString("N"));

    public override string ToString() => Id;
}

/// <summary>JSON shape for <see cref="ElementRef" />: <c>{"__raskRef__":"id"}</c>, matched by the client reviver.</summary>
internal sealed class ElementRefJsonConverter : JsonConverter<ElementRef>
{
    public override ElementRef Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? id = null;
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType == JsonTokenType.PropertyName
                    && reader.GetString() == ElementRef.Marker)
                {
                    reader.Read();
                    id = reader.GetString();
                }
                else
                {
                    reader.Skip();
                }
            }
        }

        return new ElementRef(id ?? string.Empty);
    }

    public override void Write(Utf8JsonWriter writer, ElementRef value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString(ElementRef.Marker, value.Id ?? string.Empty);
        writer.WriteEndObject();
    }
}

/// <summary>
///     Built-in element-ref operations over <see cref="IJSRuntime" />. Each passes the ref to a
///     framework JS helper (<c>__raskEl.*</c>) that receives the resolved DOM element.
/// </summary>
public static class ElementRefInterop
{
    /// <summary>Focus the element.</summary>
    public static ValueTask FocusAsync(this ElementRef element, IJSRuntime js) =>
        js.InvokeVoidAsync("__raskEl.focus", element);

    /// <summary>Remove focus from the element.</summary>
    public static ValueTask BlurAsync(this ElementRef element, IJSRuntime js) =>
        js.InvokeVoidAsync("__raskEl.blur", element);

    /// <summary>Scroll the element into view (smooth, nearest).</summary>
    public static ValueTask ScrollIntoViewAsync(this ElementRef element, IJSRuntime js) =>
        js.InvokeVoidAsync("__raskEl.scrollIntoView", element);
}
