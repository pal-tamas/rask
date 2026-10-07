using Microsoft.AspNetCore.Components;
using Rask.Core;

namespace Rask.Blazor;

/// <summary>One of a hosted component's event handlers, as its render tree declared it.</summary>
/// <param name="Id">Blazor's id for the handler.</param>
/// <param name="EventName">The DOM event, without the <c>on</c> prefix.</param>
/// <param name="ArgsType">The event args the handler takes; <see cref="EventArgs" /> when it takes none.</param>
/// <param name="ValueKind">What a <c>change</c> on its element reports.</param>
internal sealed record BlazorHandler(ulong Id, string EventName, Type ArgsType, BlazorValueKind ValueKind)
{
    private bool CarriesValue => EventName is "change" or "input";

    /// <summary>Whether the args the handler takes can be built from what Rask receives for the event.</summary>
    public bool CanBind => CarriesValue
        ? ArgsType == typeof(ChangeEventArgs) || ArgsType == typeof(EventArgs)
        : BlazorEventArgs.CanMap(ArgsType);

    /// <summary>The Rask handler that builds the args from an inbound frame and hands them to <paramref name="dispatch" />.</summary>
    /// <remarks>
    ///     Rask routes a frame by the delegate's SHAPE, so the shape is the choice: a string or a list of
    ///     strings rides the value channel (which is what carries <c>@bind</c>), an <see cref="Event" />
    ///     takes the DOM event's fields, and a handler that reads nothing asks for nothing.
    /// </remarks>
    public Delegate Bind(Func<EventArgs, Task> dispatch)
    {
        if (!CarriesValue)
        {
            return ArgsType == typeof(EventArgs)
                ? (Func<Task>)(() => dispatch(EventArgs.Empty))
                : (Func<Event, Task>)(e => dispatch(BlazorEventArgs.From(ArgsType, e)));
        }

        // Blazor's binder casts rather than parses: a checkbox wants a bool, a multi-select a string[].
        // Only on `change` — that is the frame the client sends either on.
        return (EventName, ValueKind) switch
        {
            ("change", BlazorValueKind.Checked) => (Func<string, Task>)(
                v => dispatch(new ChangeEventArgs { Value = string.Equals(v, "true", StringComparison.Ordinal) })),
            ("change", BlazorValueKind.Values) => (Func<IReadOnlyList<string>, Task>)(
                v => dispatch(new ChangeEventArgs { Value = v.ToArray() })),
            _ => (Func<string, Task>)(v => dispatch(new ChangeEventArgs { Value = v })),
        };
    }
}
