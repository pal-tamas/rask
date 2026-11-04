using Microsoft.AspNetCore.Components;
using Rask.Core;
using Rask.Core.Live;

namespace Rask.Blazor;

/// <summary>One of a hosted component's event handlers, as its render tree declared it.</summary>
/// <param name="Id">Blazor's id for the handler.</param>
/// <param name="EventName">The DOM event, without the <c>on</c> prefix.</param>
/// <param name="ArgsType">The event args the handler takes; <see cref="EventArgs" /> when it takes none.</param>
/// <param name="ValueKind">What a <c>change</c> on its element reports.</param>
/// <param name="Control">The button, input, select or textarea it is on, or null for any other element.</param>
internal sealed record BlazorHandler(
    ulong Id, string EventName, Type ArgsType, BlazorValueKind ValueKind, BlazorControl? Control = null)
{
    private bool CarriesValue => EventName is "change" or "input";

    private bool Submits => EventName is "submit";

    /// <summary>Whether the args the handler takes can be built from what Rask receives for the event.</summary>
    /// <remarks>Blazor declares <c>onsubmit</c> with plain <see cref="EventArgs" />, which is all a submit hands over.</remarks>
    public bool CanBind => this switch
    {
        { Submits: true } => ArgsType == typeof(EventArgs),
        { CarriesValue: true } => ArgsType == typeof(ChangeEventArgs) || ArgsType == typeof(EventArgs),
        _ => BlazorEventArgs.CanMap(ArgsType),
    };

    /// <summary>The Rask handler that builds the args from an inbound frame and hands them to <paramref name="dispatch" />.</summary>
    /// <remarks>
    ///     Rask routes a frame by the delegate's SHAPE, so the shape is the choice: a string or a list of
    ///     strings rides the value channel (which is what carries <c>@bind</c>), an <see cref="Event" />
    ///     takes the DOM event's fields, a handler that reads nothing asks for nothing, and a submit takes the
    ///     form channel's <see cref="FormData" /> — the only shape a <c>submit</c> frame is routed to.
    /// </remarks>
    public Delegate Bind(Func<EventArgs, Task> dispatch)
    {
        // What was posted is not handed over: Blazor's submit carries no fields, and the bound values have
        // arrived already, each through its control's own `change`, which the browser fires before the submit.
        if (Submits)
        {
            return (Func<FormData, Task>)(_ => dispatch(EventArgs.Empty));
        }

        if (!CarriesValue)
        {
            return ArgsType == typeof(EventArgs)
                ? (Func<Task>)(() => dispatch(EventArgs.Empty))
                : (Func<Event, Task>)(e => dispatch(BlazorEventArgs.From(ArgsType, e)));
        }

        // Blazor's binder casts rather than parses: a checkbox wants a bool, a multi-select a string[].
        // Only on `change` — that is the frame the client sends either on, and the one BlazorFrameWriter writes
        // an `input` handler on such a control as.
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
