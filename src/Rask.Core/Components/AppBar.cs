namespace Rask.Core.Components;

/// <summary>
///     The bar across the top of a screen — a title, an optional leading action, and optional trailing
///     actions. One declaration serves every host: the web hosts render a landmark region with real buttons,
///     and a native shell projects it to a <c>UINavigationBar</c> (iOS) / top app bar (Android), emitting no
///     HTML at all.
/// </summary>
/// <example>
///     <code>
/// public sealed class TodosScreen : Screen
/// {
///     protected override string Route => "/todos";
///
///     protected override Component? HeaderBar =>
///         AppBar.Title("Todos").Trailing([BarButton.Icon(BarIcon.Add).Title("New").OnClick(Add)]);
///
///     protected override Component? Render() => Div[/* the body */];
/// }
///     </code>
/// </example>
/// <remarks>
///     <para>
///         <b>Web markup.</b> A <c>div</c> with <c>role="banner"</c> — the same landmark a <c>&lt;header&gt;</c>
///         maps to in the accessibility tree — carrying <c>.rask-header-bar</c>, with the title in
///         <c>.rask-header-title</c> and the actions in <c>.rask-header-leading</c> /
///         <c>.rask-header-trailing</c>. Core ships no stylesheet: the class names are the styling contract.
///     </para>
///     <para>
///         For platform-exact native chrome (segmented titles, overflow menus, per-bar tinting) reach for the
///         <c>Rask.Native</c> family (<c>NativeHeaderBar</c>) instead — it is the native-only escape hatch, and
///         a screen that uses it no longer compiles for the web heads. This type is the portable subset.
///     </para>
/// </remarks>
public sealed partial class AppBar : Component
{
    /// <summary>
    ///     The bar's title. Shown centred (iOS) / leading (Android) per platform convention on native.
    ///     <c>new</c> because a markup host resolves the bare <c>Title</c> to the chain entry for the
    ///     <c>&lt;title&gt;</c> component — the same shadowing <c>NativeHeaderBar</c> declares.
    /// </summary>
    public new string? Title { get; set; }

    /// <summary>An optional leading action, shown at the start of the bar.</summary>
    public BarButton? Leading { get; set; }

    /// <summary>Optional trailing actions, shown at the end of the bar.</summary>
    public IReadOnlyList<BarButton>? Trailing { get; set; }

    protected override Component? Render()
    {
        // On a native head the bar is a real platform widget the host builds from these properties; markup
        // here would leave a duplicate header inside the WebView underneath it.
        if (IsNative)
        {
            return null;
        }

        var children = new List<Component>(3);
        if (Leading is { } leading)
        {
            children.Add(Div.Class("rask-header-leading")[leading]);
        }

        if (Title is { Length: > 0 } heading)
        {
            children.Add(Div.Class("rask-header-title")[heading]);
        }

        if (Trailing is { Count: > 0 } trailing)
        {
            children.Add(Div.Class("rask-header-trailing")[trailing]);
        }

        // role="banner" rather than a <header> tag: Rask.Core deliberately holds only the tags its own engine
        // builds (the HTML family lives in Rask.Html), and the ARIA role is what assistive technology reads
        // off a <header> anyway.
        return Div.Class("rask-header-bar").Role("banner")[children];
    }
}
