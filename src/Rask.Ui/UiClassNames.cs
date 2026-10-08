namespace Rask;

/// <summary>
/// Every daisyUI class the kit writes, spelled out.
/// </summary>
/// <remarks>
/// <para>
/// <b>These have to be complete literals, and that is the entire reason this file exists.</b> daisyUI 5
/// emits a component's CSS only where Tailwind can SEE its class name in the scanned source, so building
/// one by concatenation — <c>"btn-" + tone</c> — produces a name no scanner ever reads. The class is then
/// absent from the compiled sheet and the component renders with no styling whatsoever: not misaligned,
/// not the wrong colour, unstyled. Nothing reports it. The build is green, the markup carries exactly the
/// class the call site asked for, and only a browser shows the difference.
/// </para>
/// <para>
/// Gathering them here rather than scattering the switches through the components keeps that rule in one
/// reviewable place, and lets <c>UiClassNamesTests</c> check every literal against the sheet the build
/// actually produced.
/// </para>
/// <para>
/// A member a component has no form for returns the empty string, so composing a class list always yields
/// valid markup rather than a dangling suffix.
/// </para>
/// </remarks>
internal static class UiClassNames
{
    internal static string ButtonTone(Ui.Tone value) => value switch
    {
        Ui.Tone.Neutral => "btn-neutral",
        Ui.Tone.Primary => "btn-primary",
        Ui.Tone.Secondary => "btn-secondary",
        Ui.Tone.Accent => "btn-accent",
        Ui.Tone.Info => "btn-info",
        Ui.Tone.Success => "btn-success",
        Ui.Tone.Warning => "btn-warning",
        Ui.Tone.Error => "btn-error",
        _ => "",
    };

    internal static string ButtonVariant(Ui.Variant value) => value switch
    {
        Ui.Variant.Outline => "btn-outline",
        Ui.Variant.Soft => "btn-soft",
        Ui.Variant.Dash => "btn-dash",
        Ui.Variant.Ghost => "btn-ghost",
        Ui.Variant.Link => "btn-link",
        _ => "",
    };

    internal static string ButtonSize(Ui.Size value) => value switch
    {
        Ui.Size.Xs => "btn-xs",
        Ui.Size.Sm => "btn-sm",
        Ui.Size.Md => "btn-md",
        Ui.Size.Lg => "btn-lg",
        Ui.Size.Xl => "btn-xl",
        _ => "",
    };

    internal static string FileInputTone(Ui.Tone value) => value switch
    {
        Ui.Tone.Neutral => "file-input-neutral",
        Ui.Tone.Primary => "file-input-primary",
        Ui.Tone.Secondary => "file-input-secondary",
        Ui.Tone.Accent => "file-input-accent",
        Ui.Tone.Info => "file-input-info",
        Ui.Tone.Success => "file-input-success",
        Ui.Tone.Warning => "file-input-warning",
        Ui.Tone.Error => "file-input-error",
        _ => "",
    };

    internal static string FileInputVariant(Ui.Variant value) => value switch
    {
        Ui.Variant.Ghost => "file-input-ghost",
        _ => "",
    };

    internal static string FileInputSize(Ui.Size value) => value switch
    {
        Ui.Size.Xs => "file-input-xs",
        Ui.Size.Sm => "file-input-sm",
        Ui.Size.Md => "file-input-md",
        Ui.Size.Lg => "file-input-lg",
        Ui.Size.Xl => "file-input-xl",
        _ => "",
    };

    internal static string LoadingSize(Ui.Size value) => value switch
    {
        Ui.Size.Xs => "loading-xs",
        Ui.Size.Sm => "loading-sm",
        Ui.Size.Md => "loading-md",
        Ui.Size.Lg => "loading-lg",
        Ui.Size.Xl => "loading-xl",
        _ => "",
    };

    internal static string StatusTone(Ui.Tone value) => value switch
    {
        Ui.Tone.Neutral => "status-neutral",
        Ui.Tone.Primary => "status-primary",
        Ui.Tone.Secondary => "status-secondary",
        Ui.Tone.Accent => "status-accent",
        Ui.Tone.Info => "status-info",
        Ui.Tone.Success => "status-success",
        Ui.Tone.Warning => "status-warning",
        Ui.Tone.Error => "status-error",
        _ => "",
    };

    internal static string StatusSize(Ui.Size value) => value switch
    {
        Ui.Size.Xs => "status-xs",
        Ui.Size.Sm => "status-sm",
        Ui.Size.Md => "status-md",
        Ui.Size.Lg => "status-lg",
        Ui.Size.Xl => "status-xl",
        _ => "",
    };

    internal static string StepTone(Ui.Tone value) => value switch
    {
        Ui.Tone.Neutral => "step-neutral",
        Ui.Tone.Primary => "step-primary",
        Ui.Tone.Secondary => "step-secondary",
        Ui.Tone.Accent => "step-accent",
        Ui.Tone.Info => "step-info",
        Ui.Tone.Success => "step-success",
        Ui.Tone.Warning => "step-warning",
        Ui.Tone.Error => "step-error",
        _ => "",
    };

    internal static string TabsSize(Ui.Size value) => value switch
    {
        Ui.Size.Xs => "tabs-xs",
        Ui.Size.Sm => "tabs-sm",
        Ui.Size.Md => "tabs-md",
        Ui.Size.Lg => "tabs-lg",
        Ui.Size.Xl => "tabs-xl",
        _ => "",
    };

    internal static string MenuSize(Ui.Size value) => value switch
    {
        Ui.Size.Xs => "menu-xs",
        Ui.Size.Sm => "menu-sm",
        Ui.Size.Md => "menu-md",
        Ui.Size.Lg => "menu-lg",
        Ui.Size.Xl => "menu-xl",
        _ => "",
    };

    internal static string TableSize(Ui.Size value) => value switch
    {
        Ui.Size.Xs => "table-xs",
        Ui.Size.Sm => "table-sm",
        Ui.Size.Md => "table-md",
        Ui.Size.Lg => "table-lg",
        Ui.Size.Xl => "table-xl",
        _ => "",
    };

    /// <summary>The classes that hide a data-grid column below a breakpoint.</summary>
    /// <remarks>
    /// Table mode only. <c>sm:</c> comes first, so below <c>sm</c> — where the grid lists every cell as its own
    /// labelled line — nothing is hidden; <see cref="Ui.Breakpoint.Sm" /> is no change, because the table starts
    /// there. Variants only, never a bare <c>hidden</c>, for the cross-sheet reason Ui.DataGrid's cells record.
    /// </remarks>
    internal static string ColumnShowFrom(Ui.Breakpoint value) => value switch
    {
        Ui.Breakpoint.Md => "sm:max-md:hidden",
        Ui.Breakpoint.Lg => "sm:max-lg:hidden",
        Ui.Breakpoint.Xl => "sm:max-xl:hidden",
        _ => "",
    };

    /// <summary>The background a data-grid row takes for a tone.</summary>
    /// <remarks>A tint, not a fill: the row's text stays the body ink, so every tone reads at the same contrast.</remarks>
    internal static string RowTone(Ui.Tone value) => value switch
    {
        Ui.Tone.Neutral => "bg-neutral/10",
        Ui.Tone.Primary => "bg-primary/10",
        Ui.Tone.Secondary => "bg-secondary/10",
        Ui.Tone.Accent => "bg-accent/10",
        Ui.Tone.Info => "bg-info/10",
        Ui.Tone.Success => "bg-success/10",
        Ui.Tone.Warning => "bg-warning/10",
        Ui.Tone.Error => "bg-error/10",
        _ => "",
    };

    internal static string DockSize(Ui.Size value) => value switch
    {
        Ui.Size.Xs => "dock-xs",
        Ui.Size.Sm => "dock-sm",
        Ui.Size.Md => "dock-md",
        Ui.Size.Lg => "dock-lg",
        Ui.Size.Xl => "dock-xl",
        _ => "",
    };

    internal static string KbdSize(Ui.Size value) => value switch
    {
        Ui.Size.Xs => "kbd-xs",
        Ui.Size.Sm => "kbd-sm",
        Ui.Size.Md => "kbd-md",
        Ui.Size.Lg => "kbd-lg",
        Ui.Size.Xl => "kbd-xl",
        _ => "",
    };

    internal static string RatingSize(Ui.Size value) => value switch
    {
        Ui.Size.Xs => "rating-xs",
        Ui.Size.Sm => "rating-sm",
        Ui.Size.Md => "rating-md",
        Ui.Size.Lg => "rating-lg",
        Ui.Size.Xl => "rating-xl",
        _ => "",
    };

    internal static string SwapAnimation(Ui.SwapAnimation value) => value switch
    {
        Ui.SwapAnimation.Rotate => "swap-rotate",
        Ui.SwapAnimation.Flip => "swap-flip",
        _ => "",
    };

    internal static string AuraStyle(Ui.AuraStyle value) => value switch
    {
        Ui.AuraStyle.Glow => "aura-glow",
        Ui.AuraStyle.Dual => "aura-dual",
        Ui.AuraStyle.Holo => "aura-holo",
        Ui.AuraStyle.Rainbow => "aura-rainbow",
        Ui.AuraStyle.Gold => "aura-gold",
        Ui.AuraStyle.Silver => "aura-silver",
        _ => "",
    };

    internal static string AuraSize(Ui.Size value) => value switch
    {
        Ui.Size.Xs => "aura-xs",
        Ui.Size.Sm => "aura-sm",
        Ui.Size.Md => "aura-md",
        Ui.Size.Lg => "aura-lg",
        Ui.Size.Xl => "aura-xl",
        _ => "",
    };

    internal static string TabsStyle(Ui.TabStyle value) => value switch
    {
        Ui.TabStyle.Box => "tabs-box",
        Ui.TabStyle.Border => "tabs-border",
        Ui.TabStyle.Lift => "tabs-lift",
        _ => "",
    };

    /// <summary>Where a row of tabs sits against its panel.</summary>
    /// <remarks>
    ///     A row of tabs sits above or below its panel and nowhere else, so the horizontal members of
    ///     <see cref="Ui.Position" /> return nothing rather than a class daisyUI never defined.
    /// </remarks>
    internal static string TabsPosition(Ui.Position value) => value switch
    {
        Ui.Position.Top => "tabs-top",
        Ui.Position.Bottom => "tabs-bottom",
        _ => "",
    };

    internal static string MaskShape(Ui.MaskShape value) => value switch
    {
        Ui.MaskShape.Squircle => "mask-squircle",
        Ui.MaskShape.Star => "mask-star",
        Ui.MaskShape.Star2 => "mask-star-2",
        Ui.MaskShape.Heart => "mask-heart",
        Ui.MaskShape.Hexagon => "mask-hexagon",
        Ui.MaskShape.Hexagon2 => "mask-hexagon-2",
        Ui.MaskShape.Pentagon => "mask-pentagon",
        Ui.MaskShape.Decagon => "mask-decagon",
        Ui.MaskShape.Diamond => "mask-diamond",
        Ui.MaskShape.Triangle => "mask-triangle",
        Ui.MaskShape.Triangle2 => "mask-triangle-2",
        Ui.MaskShape.Triangle3 => "mask-triangle-3",
        Ui.MaskShape.Triangle4 => "mask-triangle-4",
        Ui.MaskShape.Half1 => "mask-half-1",
        Ui.MaskShape.Half2 => "mask-half-2",
        _ => "mask-circle",
    };

    internal static string LoadingShape(Ui.LoadingShape value) => value switch
    {
        Ui.LoadingShape.Dots => "loading-dots",
        Ui.LoadingShape.Ring => "loading-ring",
        Ui.LoadingShape.Ball => "loading-ball",
        Ui.LoadingShape.Bars => "loading-bars",
        Ui.LoadingShape.Infinity => "loading-infinity",
        _ => "loading-spinner",
    };

    /// <summary>The classes that keep a sidebar in the page's flow from a breakpoint up.</summary>
    /// <remarks>
    ///     The width from which a sidebar sits in the page's flow instead of sliding over it. Every member a complete
    ///     literal: <c>"lg:" + "drawer-open"</c> is invisible to Tailwind's scan, and the sidebar would never open.
    /// </remarks>
    internal static string SidebarInFlowFrom(Ui.Breakpoint value) => value switch
    {
        Ui.Breakpoint.Sm => "sm:drawer-open",
        Ui.Breakpoint.Md => "md:drawer-open",
        Ui.Breakpoint.Lg => "lg:drawer-open",
        Ui.Breakpoint.Xl => "xl:drawer-open",
        _ => "lg:drawer-open",
    };

    /// <summary>The classes that hide an element from a breakpoint up.</summary>
    /// <remarks>The toggle is only needed while the sidebar slides over the page, so it hides where the sidebar docks.</remarks>
    internal static string HiddenFrom(Ui.Breakpoint value) => value switch
    {
        Ui.Breakpoint.Sm => "sm:hidden",
        Ui.Breakpoint.Md => "md:hidden",
        Ui.Breakpoint.Lg => "lg:hidden",
        Ui.Breakpoint.Xl => "xl:hidden",
        _ => "lg:hidden",
    };

    /// <summary>The classes that show an element from a breakpoint up.</summary>
    /// <remarks>
    ///     The mirror of <see cref="HiddenFrom" />, for the collapse control: narrowing the sidebar to a rail only
    ///     means anything once it is DOCKED, so the control is hidden until then. ONE class name, as every member
    ///     here is — the call site pairs it with its own <c>hidden</c>, because a member returning two names would
    ///     be a name built by concatenation in everything but spelling, which is what this file exists to avoid.
    /// </remarks>
    internal static string ShownFrom(Ui.Breakpoint value) => value switch
    {
        Ui.Breakpoint.Sm => "sm:inline-flex",
        Ui.Breakpoint.Md => "md:inline-flex",
        Ui.Breakpoint.Lg => "lg:inline-flex",
        Ui.Breakpoint.Xl => "xl:inline-flex",
        _ => "lg:inline-flex",
    };

    /// <summary>The ink a value takes when it reports a problem; null for a value that reports none.</summary>
    internal static string? ValueTone(Ui.Tone? value) => value switch
    {
        Ui.Tone.Error => "text-ui-danger-ink",
        Ui.Tone.Warning => "text-ui-warn-ink",
        _ => null,
    };

    /// <summary>The edge a drawer opens from.</summary>
    /// <remarks>
    ///     A drawer opens from the left or the right edge, and daisyUI's one class for it is <c>drawer-end</c>,
    ///     so only <see cref="Ui.Position.Right" /> writes anything.
    /// </remarks>
    internal static string DrawerPosition(Ui.Position value) => value switch
    {
        Ui.Position.Right => "drawer-end",
        _ => "",
    };

    /// <summary>The frame size of an avatar.</summary>
    /// <remarks>
    ///     daisyUI's avatar has no size classes of its own — its docs size the inner box with a width
    ///     utility — so the literals live here, where Tailwind can see them.
    /// </remarks>
    internal static string AvatarSize(Ui.Size value) => value switch
    {
        Ui.Size.Xs => "w-6",
        Ui.Size.Sm => "w-8",
        Ui.Size.Lg => "w-16",
        Ui.Size.Xl => "w-24",
        _ => "w-10",
    };
}
