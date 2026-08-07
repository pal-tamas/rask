using Rask.Core.Components;
using Rask.Core.Routing;

namespace Rask.Core;

/// <summary>
///     PROTOTYPE — the builder surface's setters, paired with <see cref="Component" />'s entry properties.
/// </summary>
/// <remarks>
///     <para>
///         Extension methods, not members of the component types, and that is load-bearing: a method
///         declared inside the type cannot share a name with the property it sets (CS0102), so an
///         in-type setter would have to be spelled <c>WithClass</c>. As an extension it may share the
///         name, because C#'s invocable-member rule skips the non-invocable property and falls through
///         to the extension. The trade is navigation — F12 lands here rather than on the component.
///     </para>
///     <para>
///         That rule does NOT rescue delegate-typed properties: <c>OnClickAsync</c> is invocable, so an
///         extension of the same name would lose to it and fail with CS1593. Callback setters therefore
///         drop the <c>On</c> prefix — <c>.ClickAsync(…)</c> sets <c>OnClickAsync</c>.
///     </para>
///     <para>
///         Callbacks are routed through <see cref="AutoCallback" /> exactly as the generated factories do,
///         so a handler still re-renders the component that defined it. Skipping that here would silently
///         break reactivity for anything written in the new syntax.
///     </para>
/// </remarks>
public static class BuilderSetters
{
    // ---- Component ----------------------------------------------------------------------------

    /// <summary>Sets the reconciliation identity — the builder equivalent of the factory's <c>Key:</c>.</summary>
    public static T Key<T>(this T c, object? value) where T : Component
    {
        c.Key = value;
        return c;
    }

    // ---- Element: universal attributes ---------------------------------------------------------

    public static T Class<T>(this T e, string? value) where T : Element
    {
        e.Class = value;
        return e;
    }

    public static T Id<T>(this T e, string? value) where T : Element
    {
        e.Id = value;
        return e;
    }

    public static T Style<T>(this T e, string? value) where T : Element
    {
        e.Style = value;
        return e;
    }

    public static T Title<T>(this T e, string? value) where T : Element
    {
        e.Title = value;
        return e;
    }

    public static T Role<T>(this T e, string? value) where T : Element
    {
        e.Role = value;
        return e;
    }

    // ---- Element: events ------------------------------------------------------------------------
    //
    // NOT routed through AutoCallback: element handlers are forwarded straight to the DOM, where the
    // existing handler-owner resolution already re-renders the parent. The factory generator restricts
    // wrapping to non-Element components for exactly this reason — wrapping here would add a delegate
    // allocation per handler per render on the hot path. See AutoCallback's remarks.
    //
    // Still prefix-free (`.Click`, not `.OnClick`) because Element.OnClick is a delegate-typed property
    // and would win over a same-named method (CS1593). Handler/HandlerAsync is the fix for that — see
    // BuilderCallback.cs — but applying it to Element changes the factory's parameter types, so it
    // belongs to the clean break rather than to this additive slice.

    public static T Click<T>(this T e, Callback? handler) where T : Element
    {
        e.OnClick = handler;
        return e;
    }

    public static T ClickAsync<T>(this T e, CallbackAsync? handler) where T : Element
    {
        e.OnClickAsync = handler;
        return e;
    }

    // ---- Tag-specific --------------------------------------------------------------------------

    public static Button Type(this Button b, string? value)
    {
        b.Type = value;
        return b;
    }

    public static Button Disabled(this Button b, bool? value = true)
    {
        b.Disabled = value;
        return b;
    }

    /// <summary>Sets <see cref="Components.NavLink.Href" />. Named <c>To</c> because it reads better at the call site.</summary>
    public static NavLink To(this NavLink n, RouteUrl? value)
    {
        n.Href = value;
        return n;
    }

    public static Img Src(this Img i, string? value)
    {
        i.Src = value;
        return i;
    }

    public static Img Alt(this Img i, string? value)
    {
        i.Alt = value;
        return i;
    }
}
