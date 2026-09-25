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
