using System.Diagnostics.CodeAnalysis;
using Microsoft.JSInterop;

namespace Rask.Core;

// What the members generated onto IElementRef<T> (ElementRefMembers, from MDN's data) call: one generic helper each
// for an operation, a read and a write, in rask-api.ts. The member's name always comes from the generated code, so a
// ref can only ever reach the members MDN lists for its element.
internal static class ElementRefCall
{
    // What JSInterop's InvokeAsync<T> asks of a result type, so the trimmer keeps it deserializable.
    private const DynamicallyAccessedMemberTypes Json =
        DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties;

    internal static ValueTask Call(ElementRef element, string name) =>
        element.Runtime.InvokeVoidAsync("__raskEl.call", element, name);

    internal static ValueTask Call(ElementRef element, string name, object? arg0) =>
        element.Runtime.InvokeVoidAsync("__raskEl.call", element, name, arg0);

    internal static ValueTask Call(ElementRef element, string name, object?[] args) =>
        element.Runtime.InvokeVoidAsync("__raskEl.call", [element, name, .. args]);

    internal static ValueTask<T> Call<[DynamicallyAccessedMembers(Json)] T>(ElementRef element, string name) =>
        element.Runtime.InvokeAsync<T>("__raskEl.call", element, name);

    internal static ValueTask<T> Call<[DynamicallyAccessedMembers(Json)] T>(ElementRef element, string name, object? arg0) =>
        element.Runtime.InvokeAsync<T>("__raskEl.call", element, name, arg0);

    internal static ValueTask<T> Call<[DynamicallyAccessedMembers(Json)] T>(ElementRef element, string name, object?[] args) =>
        element.Runtime.InvokeAsync<T>("__raskEl.call", [element, name, .. args]);

    internal static ValueTask<T> Get<[DynamicallyAccessedMembers(Json)] T>(ElementRef element, string name) =>
        element.Runtime.InvokeAsync<T>("__raskEl.get", element, name);

    internal static ValueTask Set(ElementRef element, string name, object? value) =>
        element.Runtime.InvokeVoidAsync("__raskEl.set", element, name, value);
}
