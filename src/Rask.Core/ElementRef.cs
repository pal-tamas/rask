using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Rask.Core.Live;

namespace Rask.Core;

/// <summary>
///     A stable handle to a rendered DOM element: for calling the element's own DOM members from C#, and
///     for handing the element to JavaScript. Rask's analogue of Blazor's <c>ElementReference</c>.
/// </summary>
/// <remarks>
///     <para>
///         Create one in a field, so its id is stable across renders, and attach it to an element with
///         <c>.Ref(…)</c>, which stamps <c>data-rask-ref="{id}"</c> onto it. Typed to the element's MDN
///         interface, it carries that interface's members, generated from MDN:
///         <code>
///         private readonly ElementRef&lt;HTMLDialogElement&gt; _dialog = new();
///         protected override Component? Render() => Dialog.Ref(_dialog)[…];
///         await _dialog.ShowModal();          // MDN's showModal()
///         var open = await _dialog.Open;      // MDN's open
///         </code>
///     </para>
///     <para>
///         An untyped <see cref="ElementRef" /> carries <c>Element</c>'s members. Either one passes to
///         <see cref="IJSRuntime" />, and the client resolves it to the live element first:
///         <c>await js.InvokeVoidAsync("MyChart.init", _chart, data)</c>.
///     </para>
///     <para>
///         Wire format: an <see cref="ElementRef" /> serializes to <c>{"__raskRef__":"id"}</c>;
///         the runtime's JSON reviver swaps that for <c>document.querySelector('[data-rask-ref="id"]')</c>.
///         Ids are GUID hex, so they are always selector-safe.
///     </para>
/// </remarks>
// A reference type (not a struct): the only allocation is on ElementRef.New() — once per ref'd
// element, typically a field initializer — so element refs cost nothing on the render hot path.
// (A struct here would force every element factory to carry a struct optional parameter, which
// measurably regressed the counter-render allocation pin; a nullable-reference param is free.)
[JsonConverter(typeof(ElementRefJsonConverter))]
public class ElementRef : IElementRef<Element>
{
    internal const string Marker = "__raskRef__";

    internal ElementRef(string id) => Id = id;

    /// <summary>The opaque element id, emitted as <c>data-rask-ref</c> and matched client-side.</summary>
    public string Id { get; }

    // The element this ref was last put on: its session is the one whose browser the ref's members run in.
    internal Element? Element { get; private set; }

    ElementRef IElementRef<Element>.Target => this;

    Element? IElementRef<Element>.Attached => Element;

    /// <summary>Mint a new ref with a unique, selector-safe id. Store it in a field for stability.</summary>
    public static ElementRef New() => new(Guid.NewGuid().ToString("N"));

    public override string ToString() => Id;

    internal void AttachTo(Element element)
    {
        if (!Accepts(element))
        {
            throw new InvalidOperationException(
                $"An ElementRef<{TypedTo}> is on a <{element.TagNameInternal}>, which is a {element.GetType().Name}. "
                + "Type the ref to the element it is on.");
        }

        Element = element;
    }

    private protected virtual bool Accepts(Element element) => true;

    private protected virtual string TypedTo => nameof(Rask.Core.Element);

    // The browser this ref's element lives in: its session's, else the one handling the current event.
    internal IJSRuntime Runtime =>
        ((Element?.RenderHandle as LiveSessionBase)?.Services ?? AmbientServices.Current)?.GetService<IJSRuntime>()
        ?? throw new InvalidOperationException(
            "An element ref reached for its element before the element was on a page. Call it from an event handler or "
            + "from OnRendered, once the element is live.");
}

/// <summary>
///     An <see cref="ElementRef" /> typed to the element's MDN interface, which carries that interface's DOM members
///     (<c>ShowModal()</c> on an <c>ElementRef&lt;HTMLDialogElement&gt;</c>, <c>Play()</c> on a video's).
/// </summary>
/// <typeparam name="T">The element type it goes on: a ref to a <c>HTMLDialogElement</c> goes on a <c>Dialog</c>.</typeparam>
[JsonConverter(typeof(ElementRefJsonConverter))]
public sealed class ElementRef<T> : ElementRef, IElementRef<T>
    where T : Element
{
    /// <summary>Mint a new ref with a unique, selector-safe id. Store it in a field for stability.</summary>
    public ElementRef()
        : base(Guid.NewGuid().ToString("N"))
    {
    }

    ElementRef IElementRef<T>.Target => this;

    T? IElementRef<T>.Attached => Element as T;

    private protected override bool Accepts(Element element) => element is T;

    private protected override string TypedTo => typeof(T).Name;
}
