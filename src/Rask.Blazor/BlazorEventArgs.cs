using Rask.Core;
using Web = Microsoft.AspNetCore.Components.Web;

namespace Rask.Blazor;

/// <summary>Builds the event args a hosted Blazor handler asked for out of the event Rask received.</summary>
/// <remarks>
///     A switch over the closed set <c>Microsoft.AspNetCore.Components.Web</c> defines, never
///     <c>Activator.CreateInstance</c>: a WASM app publishes trimmed, and naming each type here is also
///     what roots it for the handler's own cast.
/// </remarks>
internal static class BlazorEventArgs
{
    private static readonly Event Blank = new();

    /// <summary>Whether <paramref name="expected" /> is an args type <see cref="From" /> can build.</summary>
    public static bool CanMap(Type expected) => Build(expected, Blank) is not null;

    /// <summary>The <paramref name="expected" /> args, filled from <paramref name="e" />.</summary>
    public static EventArgs From(Type expected, Event e) => Build(expected, e) ?? EventArgs.Empty;

    // Exact type equality, never `is`: PointerEventArgs derives from MouseEventArgs, and a subtype test
    // would hand a handler expecting the derived one an instance of its base. Order is presentation only.
    private static EventArgs? Build(Type expected, Event e) => expected switch
    {
        _ when expected == typeof(EventArgs) => EventArgs.Empty,
        _ when expected == typeof(Web.MouseEventArgs) => Mouse(new Web.MouseEventArgs(), e),
        _ when expected == typeof(Web.PointerEventArgs) => Pointer(e),
        _ when expected == typeof(Web.WheelEventArgs) => Wheel(e),
        _ when expected == typeof(Web.DragEventArgs) => Drag(e),
        _ when expected == typeof(Web.KeyboardEventArgs) => Keyboard(e),
        _ when expected == typeof(Web.TouchEventArgs) => Touch(e),
        _ when expected == typeof(Web.FocusEventArgs) => new Web.FocusEventArgs { Type = e.Type },
        _ when expected == typeof(Web.ClipboardEventArgs) => new Web.ClipboardEventArgs { Type = e.Type },

        // Rask models these two as a plain Event, so the type is all there is to hand over.
        _ when expected == typeof(Web.ProgressEventArgs) => new Web.ProgressEventArgs { Type = e.Type },
        _ when expected == typeof(Web.ErrorEventArgs) => new Web.ErrorEventArgs { Type = e.Type },
        _ => null,
    };

    // The fields MouseEventArgs declares, shared by the three args types that derive from it.
    private static T Mouse<T>(T args, Event e)
        where T : Web.MouseEventArgs
    {
        args.Type = e.Type;
        if (e is not MouseEvent m)
        {
            return args;
        }

        args.Detail = m.Detail;
        args.ScreenX = m.ScreenX;
        args.ScreenY = m.ScreenY;
        args.ClientX = m.ClientX;
        args.ClientY = m.ClientY;
        args.OffsetX = m.OffsetX;
        args.OffsetY = m.OffsetY;
        args.PageX = m.PageX;
        args.PageY = m.PageY;
        args.MovementX = m.MovementX;
        args.MovementY = m.MovementY;
        args.Button = m.Button;
        args.Buttons = m.Buttons;
        args.CtrlKey = m.CtrlKey;
        args.ShiftKey = m.ShiftKey;
        args.AltKey = m.AltKey;
        args.MetaKey = m.MetaKey;
        return args;
    }

    private static Web.PointerEventArgs Pointer(Event e)
    {
        var args = Mouse(new Web.PointerEventArgs(), e);
        if (e is PointerEvent p)
        {
            args.PointerId = p.PointerId;
            args.Width = (float)p.Width;
            args.Height = (float)p.Height;
            args.Pressure = (float)p.Pressure;
            args.TiltX = p.TiltX;
            args.TiltY = p.TiltY;
            args.PointerType = p.PointerType;
            args.IsPrimary = p.IsPrimary;
        }

        return args;
    }

    private static Web.WheelEventArgs Wheel(Event e)
    {
        var args = Mouse(new Web.WheelEventArgs(), e);
        if (e is WheelEvent w)
        {
            args.DeltaX = w.DeltaX;
            args.DeltaY = w.DeltaY;
            args.DeltaZ = w.DeltaZ;
            args.DeltaMode = w.DeltaMode;
        }

        return args;
    }

    private static Web.DragEventArgs Drag(Event e)
    {
        var args = Mouse(new Web.DragEventArgs(), e);
        if (e is DragEvent { DataTransfer: { } data })
        {
            args.DataTransfer = new Web.DataTransfer
            {
                DropEffect = data.DropEffect,
                EffectAllowed = data.EffectAllowed,
                Types = [.. data.Types],
            };
        }

        return args;
    }

    private static Web.KeyboardEventArgs Keyboard(Event e) => e is KeyboardEvent k
        ? new Web.KeyboardEventArgs
        {
            Type = k.Type,
            Key = k.Key,
            Code = k.Code,
            Location = k.Location,
            Repeat = k.Repeat,
            IsComposing = k.IsComposing,
            CtrlKey = k.CtrlKey,
            ShiftKey = k.ShiftKey,
            AltKey = k.AltKey,
            MetaKey = k.MetaKey,
        }
        : new Web.KeyboardEventArgs { Type = e.Type };

    private static Web.TouchEventArgs Touch(Event e) => e is TouchEvent t
        ? new Web.TouchEventArgs
        {
            Type = t.Type,
            Detail = t.Detail,
            Touches = Points(t.Touches),
            TargetTouches = Points(t.TargetTouches),
            ChangedTouches = Points(t.ChangedTouches),
            CtrlKey = t.CtrlKey,
            ShiftKey = t.ShiftKey,
            AltKey = t.AltKey,
            MetaKey = t.MetaKey,
        }
        : new Web.TouchEventArgs { Type = e.Type };

    private static Web.TouchPoint[] Points(IReadOnlyList<Touch> touches)
    {
        var points = new Web.TouchPoint[touches.Count];
        for (var i = 0; i < points.Length; i++)
        {
            var t = touches[i];
            points[i] = new Web.TouchPoint
            {
                Identifier = t.Identifier,
                ScreenX = t.ScreenX,
                ScreenY = t.ScreenY,
                ClientX = t.ClientX,
                ClientY = t.ClientY,
                PageX = t.PageX,
                PageY = t.PageY,
            };
        }

        return points;
    }
}
