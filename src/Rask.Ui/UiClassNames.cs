namespace Rask.Ui;

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
    internal static string ButtonTone(UiTone value) => value switch
    {
        UiTone.Neutral => "btn-neutral",
        UiTone.Primary => "btn-primary",
        UiTone.Secondary => "btn-secondary",
        UiTone.Accent => "btn-accent",
        UiTone.Info => "btn-info",
        UiTone.Success => "btn-success",
        UiTone.Warning => "btn-warning",
        UiTone.Error => "btn-error",
        _ => "",
    };

    internal static string ButtonVariant(UiVariant value) => value switch
    {
        UiVariant.Outline => "btn-outline",
        UiVariant.Soft => "btn-soft",
        UiVariant.Dash => "btn-dash",
        UiVariant.Ghost => "btn-ghost",
        UiVariant.Link => "btn-link",
        _ => "",
    };

    internal static string ButtonSize(UiSize value) => value switch
    {
        UiSize.Xs => "btn-xs",
        UiSize.Sm => "btn-sm",
        UiSize.Md => "btn-md",
        UiSize.Lg => "btn-lg",
        UiSize.Xl => "btn-xl",
        _ => "",
    };

    internal static string BadgeTone(UiTone value) => value switch
    {
        UiTone.Neutral => "badge-neutral",
        UiTone.Primary => "badge-primary",
        UiTone.Secondary => "badge-secondary",
        UiTone.Accent => "badge-accent",
        UiTone.Info => "badge-info",
        UiTone.Success => "badge-success",
        UiTone.Warning => "badge-warning",
        UiTone.Error => "badge-error",
        _ => "",
    };

    internal static string BadgeVariant(UiVariant value) => value switch
    {
        UiVariant.Outline => "badge-outline",
        UiVariant.Soft => "badge-soft",
        UiVariant.Dash => "badge-dash",
        UiVariant.Ghost => "badge-ghost",
        _ => "",
    };

    internal static string BadgeSize(UiSize value) => value switch
    {
        UiSize.Xs => "badge-xs",
        UiSize.Sm => "badge-sm",
        UiSize.Md => "badge-md",
        UiSize.Lg => "badge-lg",
        UiSize.Xl => "badge-xl",
        _ => "",
    };

    internal static string AlertTone(UiTone value) => value switch
    {
        UiTone.Info => "alert-info",
        UiTone.Success => "alert-success",
        UiTone.Warning => "alert-warning",
        UiTone.Error => "alert-error",
        _ => "",
    };

    internal static string AlertVariant(UiVariant value) => value switch
    {
        UiVariant.Outline => "alert-outline",
        UiVariant.Soft => "alert-soft",
        UiVariant.Dash => "alert-dash",
        _ => "",
    };

    internal static string InputTone(UiTone value) => value switch
    {
        UiTone.Neutral => "input-neutral",
        UiTone.Primary => "input-primary",
        UiTone.Secondary => "input-secondary",
        UiTone.Accent => "input-accent",
        UiTone.Info => "input-info",
        UiTone.Success => "input-success",
        UiTone.Warning => "input-warning",
        UiTone.Error => "input-error",
        _ => "",
    };

    internal static string InputVariant(UiVariant value) => value switch
    {
        UiVariant.Ghost => "input-ghost",
        _ => "",
    };

    internal static string InputSize(UiSize value) => value switch
    {
        UiSize.Xs => "input-xs",
        UiSize.Sm => "input-sm",
        UiSize.Md => "input-md",
        UiSize.Lg => "input-lg",
        UiSize.Xl => "input-xl",
        _ => "",
    };

    internal static string SelectTone(UiTone value) => value switch
    {
        UiTone.Neutral => "select-neutral",
        UiTone.Primary => "select-primary",
        UiTone.Secondary => "select-secondary",
        UiTone.Accent => "select-accent",
        UiTone.Info => "select-info",
        UiTone.Success => "select-success",
        UiTone.Warning => "select-warning",
        UiTone.Error => "select-error",
        _ => "",
    };

    internal static string SelectVariant(UiVariant value) => value switch
    {
        UiVariant.Ghost => "select-ghost",
        _ => "",
    };

    internal static string SelectSize(UiSize value) => value switch
    {
        UiSize.Xs => "select-xs",
        UiSize.Sm => "select-sm",
        UiSize.Md => "select-md",
        UiSize.Lg => "select-lg",
        UiSize.Xl => "select-xl",
        _ => "",
    };

    internal static string TextareaTone(UiTone value) => value switch
    {
        UiTone.Neutral => "textarea-neutral",
        UiTone.Primary => "textarea-primary",
        UiTone.Secondary => "textarea-secondary",
        UiTone.Accent => "textarea-accent",
        UiTone.Info => "textarea-info",
        UiTone.Success => "textarea-success",
        UiTone.Warning => "textarea-warning",
        UiTone.Error => "textarea-error",
        _ => "",
    };

    internal static string TextareaVariant(UiVariant value) => value switch
    {
        UiVariant.Ghost => "textarea-ghost",
        _ => "",
    };

    internal static string TextareaSize(UiSize value) => value switch
    {
        UiSize.Xs => "textarea-xs",
        UiSize.Sm => "textarea-sm",
        UiSize.Md => "textarea-md",
        UiSize.Lg => "textarea-lg",
        UiSize.Xl => "textarea-xl",
        _ => "",
    };

    // A chart's colours, one table per use. Tailwind's own colour utilities over daisyUI's theme colours, so a series
    // follows the theme; every one a complete literal for the reason this file exists.
    internal static string ChartStroke(UiTone value) => value switch
    {
        UiTone.Neutral => "stroke-neutral",
        UiTone.Primary => "stroke-primary",
        UiTone.Secondary => "stroke-secondary",
        UiTone.Accent => "stroke-accent",
        UiTone.Info => "stroke-info",
        UiTone.Success => "stroke-success",
        UiTone.Warning => "stroke-warning",
        UiTone.Error => "stroke-error",
        _ => "",
    };

    internal static string ChartFill(UiTone value) => value switch
    {
        UiTone.Neutral => "fill-neutral",
        UiTone.Primary => "fill-primary",
        UiTone.Secondary => "fill-secondary",
        UiTone.Accent => "fill-accent",
        UiTone.Info => "fill-info",
        UiTone.Success => "fill-success",
        UiTone.Warning => "fill-warning",
        UiTone.Error => "fill-error",
        _ => "",
    };

    // The area under a line: the line's colour, faint enough that a second area and the grid show through it.
    internal static string ChartArea(UiTone value) => value switch
    {
        UiTone.Neutral => "fill-neutral/15",
        UiTone.Primary => "fill-primary/15",
        UiTone.Secondary => "fill-secondary/15",
        UiTone.Accent => "fill-accent/15",
        UiTone.Info => "fill-info/15",
        UiTone.Success => "fill-success/15",
        UiTone.Warning => "fill-warning/15",
        UiTone.Error => "fill-error/15",
        _ => "",
    };

    // The dot beside a series' name in the legend and a tooltip.
    internal static string ChartSwatch(UiTone value) => value switch
    {
        UiTone.Neutral => "bg-neutral",
        UiTone.Primary => "bg-primary",
        UiTone.Secondary => "bg-secondary",
        UiTone.Accent => "bg-accent",
        UiTone.Info => "bg-info",
        UiTone.Success => "bg-success",
        UiTone.Warning => "bg-warning",
        UiTone.Error => "bg-error",
        _ => "",
    };

    internal static string FileInputTone(UiTone value) => value switch
    {
        UiTone.Neutral => "file-input-neutral",
        UiTone.Primary => "file-input-primary",
        UiTone.Secondary => "file-input-secondary",
        UiTone.Accent => "file-input-accent",
        UiTone.Info => "file-input-info",
        UiTone.Success => "file-input-success",
        UiTone.Warning => "file-input-warning",
        UiTone.Error => "file-input-error",
        _ => "",
    };

    internal static string FileInputVariant(UiVariant value) => value switch
    {
        UiVariant.Ghost => "file-input-ghost",
        _ => "",
    };

    internal static string FileInputSize(UiSize value) => value switch
    {
        UiSize.Xs => "file-input-xs",
        UiSize.Sm => "file-input-sm",
        UiSize.Md => "file-input-md",
        UiSize.Lg => "file-input-lg",
        UiSize.Xl => "file-input-xl",
        _ => "",
    };

    internal static string CheckboxTone(UiTone value) => value switch
    {
        UiTone.Neutral => "checkbox-neutral",
        UiTone.Primary => "checkbox-primary",
        UiTone.Secondary => "checkbox-secondary",
        UiTone.Accent => "checkbox-accent",
        UiTone.Info => "checkbox-info",
        UiTone.Success => "checkbox-success",
        UiTone.Warning => "checkbox-warning",
        UiTone.Error => "checkbox-error",
        _ => "",
    };

    internal static string CheckboxSize(UiSize value) => value switch
    {
        UiSize.Xs => "checkbox-xs",
        UiSize.Sm => "checkbox-sm",
        UiSize.Md => "checkbox-md",
        UiSize.Lg => "checkbox-lg",
        UiSize.Xl => "checkbox-xl",
        _ => "",
    };

    internal static string RadioTone(UiTone value) => value switch
    {
        UiTone.Neutral => "radio-neutral",
        UiTone.Primary => "radio-primary",
        UiTone.Secondary => "radio-secondary",
        UiTone.Accent => "radio-accent",
        UiTone.Info => "radio-info",
        UiTone.Success => "radio-success",
        UiTone.Warning => "radio-warning",
        UiTone.Error => "radio-error",
        _ => "",
    };

    internal static string RadioSize(UiSize value) => value switch
    {
        UiSize.Xs => "radio-xs",
        UiSize.Sm => "radio-sm",
        UiSize.Md => "radio-md",
        UiSize.Lg => "radio-lg",
        UiSize.Xl => "radio-xl",
        _ => "",
    };

    internal static string ToggleTone(UiTone value) => value switch
    {
        UiTone.Neutral => "toggle-neutral",
        UiTone.Primary => "toggle-primary",
        UiTone.Secondary => "toggle-secondary",
        UiTone.Accent => "toggle-accent",
        UiTone.Info => "toggle-info",
        UiTone.Success => "toggle-success",
        UiTone.Warning => "toggle-warning",
        UiTone.Error => "toggle-error",
        _ => "",
    };

    internal static string ToggleSize(UiSize value) => value switch
    {
        UiSize.Xs => "toggle-xs",
        UiSize.Sm => "toggle-sm",
        UiSize.Md => "toggle-md",
        UiSize.Lg => "toggle-lg",
        UiSize.Xl => "toggle-xl",
        _ => "",
    };

    internal static string RangeTone(UiTone value) => value switch
    {
        UiTone.Neutral => "range-neutral",
        UiTone.Primary => "range-primary",
        UiTone.Secondary => "range-secondary",
        UiTone.Accent => "range-accent",
        UiTone.Info => "range-info",
        UiTone.Success => "range-success",
        UiTone.Warning => "range-warning",
        UiTone.Error => "range-error",
        _ => "",
    };

    internal static string RangeSize(UiSize value) => value switch
    {
        UiSize.Xs => "range-xs",
        UiSize.Sm => "range-sm",
        UiSize.Md => "range-md",
        UiSize.Lg => "range-lg",
        UiSize.Xl => "range-xl",
        _ => "",
    };

    internal static string ProgressTone(UiTone value) => value switch
    {
        UiTone.Neutral => "progress-neutral",
        UiTone.Primary => "progress-primary",
        UiTone.Secondary => "progress-secondary",
        UiTone.Accent => "progress-accent",
        UiTone.Info => "progress-info",
        UiTone.Success => "progress-success",
        UiTone.Warning => "progress-warning",
        UiTone.Error => "progress-error",
        _ => "",
    };

    internal static string LinkTone(UiTone value) => value switch
    {
        UiTone.Neutral => "link-neutral",
        UiTone.Primary => "link-primary",
        UiTone.Secondary => "link-secondary",
        UiTone.Accent => "link-accent",
        UiTone.Info => "link-info",
        UiTone.Success => "link-success",
        UiTone.Warning => "link-warning",
        UiTone.Error => "link-error",
        _ => "",
    };

    internal static string LoadingSize(UiSize value) => value switch
    {
        UiSize.Xs => "loading-xs",
        UiSize.Sm => "loading-sm",
        UiSize.Md => "loading-md",
        UiSize.Lg => "loading-lg",
        UiSize.Xl => "loading-xl",
        _ => "",
    };

    internal static string StatusTone(UiTone value) => value switch
    {
        UiTone.Neutral => "status-neutral",
        UiTone.Primary => "status-primary",
        UiTone.Secondary => "status-secondary",
        UiTone.Accent => "status-accent",
        UiTone.Info => "status-info",
        UiTone.Success => "status-success",
        UiTone.Warning => "status-warning",
        UiTone.Error => "status-error",
        _ => "",
    };

    internal static string StatusSize(UiSize value) => value switch
    {
        UiSize.Xs => "status-xs",
        UiSize.Sm => "status-sm",
        UiSize.Md => "status-md",
        UiSize.Lg => "status-lg",
        UiSize.Xl => "status-xl",
        _ => "",
    };

    internal static string StepTone(UiTone value) => value switch
    {
        UiTone.Neutral => "step-neutral",
        UiTone.Primary => "step-primary",
        UiTone.Secondary => "step-secondary",
        UiTone.Accent => "step-accent",
        UiTone.Info => "step-info",
        UiTone.Success => "step-success",
        UiTone.Warning => "step-warning",
        UiTone.Error => "step-error",
        _ => "",
    };

    internal static string TooltipTone(UiTone value) => value switch
    {
        UiTone.Primary => "tooltip-primary",
        UiTone.Secondary => "tooltip-secondary",
        UiTone.Accent => "tooltip-accent",
        UiTone.Info => "tooltip-info",
        UiTone.Success => "tooltip-success",
        UiTone.Warning => "tooltip-warning",
        UiTone.Error => "tooltip-error",
        _ => "",
    };

    internal static string TabsSize(UiSize value) => value switch
    {
        UiSize.Xs => "tabs-xs",
        UiSize.Sm => "tabs-sm",
        UiSize.Md => "tabs-md",
        UiSize.Lg => "tabs-lg",
        UiSize.Xl => "tabs-xl",
        _ => "",
    };

    internal static string CardSize(UiSize value) => value switch
    {
        UiSize.Xs => "card-xs",
        UiSize.Sm => "card-sm",
        UiSize.Md => "card-md",
        UiSize.Lg => "card-lg",
        UiSize.Xl => "card-xl",
        _ => "",
    };

    internal static string MenuSize(UiSize value) => value switch
    {
        UiSize.Xs => "menu-xs",
        UiSize.Sm => "menu-sm",
        UiSize.Md => "menu-md",
        UiSize.Lg => "menu-lg",
        UiSize.Xl => "menu-xl",
        _ => "",
    };

    internal static string TableSize(UiSize value) => value switch
    {
        UiSize.Xs => "table-xs",
        UiSize.Sm => "table-sm",
        UiSize.Md => "table-md",
        UiSize.Lg => "table-lg",
        UiSize.Xl => "table-xl",
        _ => "",
    };

    /// <remarks>
    /// Table mode only. <c>sm:</c> comes first, so below <c>sm</c> — where the grid lists every cell as its own
    /// labelled line — nothing is hidden; <see cref="UiBreakpoint.Sm" /> is no change, because the table starts
    /// there. Variants only, never a bare <c>hidden</c>, for the cross-sheet reason UiDataGrid's cells record.
    /// </remarks>
    internal static string ColumnShowFrom(UiBreakpoint value) => value switch
    {
        UiBreakpoint.Md => "sm:max-md:hidden",
        UiBreakpoint.Lg => "sm:max-lg:hidden",
        UiBreakpoint.Xl => "sm:max-xl:hidden",
        _ => "",
    };

    /// <remarks>A tint, not a fill: the row's text stays the body ink, so every tone reads at the same contrast.</remarks>
    internal static string RowTone(UiTone value) => value switch
    {
        UiTone.Neutral => "bg-neutral/10",
        UiTone.Primary => "bg-primary/10",
        UiTone.Secondary => "bg-secondary/10",
        UiTone.Accent => "bg-accent/10",
        UiTone.Info => "bg-info/10",
        UiTone.Success => "bg-success/10",
        UiTone.Warning => "bg-warning/10",
        UiTone.Error => "bg-error/10",
        _ => "",
    };

    internal static string DividerTone(UiTone value) => value switch
    {
        UiTone.Neutral => "divider-neutral",
        UiTone.Primary => "divider-primary",
        UiTone.Secondary => "divider-secondary",
        UiTone.Accent => "divider-accent",
        UiTone.Info => "divider-info",
        UiTone.Success => "divider-success",
        UiTone.Warning => "divider-warning",
        UiTone.Error => "divider-error",
        _ => "",
    };

    internal static string DockSize(UiSize value) => value switch
    {
        UiSize.Xs => "dock-xs",
        UiSize.Sm => "dock-sm",
        UiSize.Md => "dock-md",
        UiSize.Lg => "dock-lg",
        UiSize.Xl => "dock-xl",
        _ => "",
    };

    internal static string KbdSize(UiSize value) => value switch
    {
        UiSize.Xs => "kbd-xs",
        UiSize.Sm => "kbd-sm",
        UiSize.Md => "kbd-md",
        UiSize.Lg => "kbd-lg",
        UiSize.Xl => "kbd-xl",
        _ => "",
    };

    internal static string RatingSize(UiSize value) => value switch
    {
        UiSize.Xs => "rating-xs",
        UiSize.Sm => "rating-sm",
        UiSize.Md => "rating-md",
        UiSize.Lg => "rating-lg",
        UiSize.Xl => "rating-xl",
        _ => "",
    };

    internal static string DropdownPosition(UiPosition value) => value switch
    {
        UiPosition.Top => "dropdown-top",
        UiPosition.Bottom => "dropdown-bottom",
        UiPosition.Left => "dropdown-left",
        UiPosition.Right => "dropdown-right",
        _ => "",
    };

    internal static string DropdownAlign(UiAlign value) => value switch
    {
        UiAlign.Start => "dropdown-start",
        UiAlign.Center => "dropdown-center",
        UiAlign.End => "dropdown-end",
        _ => "",
    };

    internal static string ModalPosition(UiModalPosition value) => value switch
    {
        UiModalPosition.Top => "modal-top",
        UiModalPosition.Middle => "modal-middle",
        UiModalPosition.Bottom => "modal-bottom",
        UiModalPosition.Start => "modal-start",
        UiModalPosition.End => "modal-end",
        _ => "",
    };

    internal static string SwapAnimation(UiSwapAnimation value) => value switch
    {
        UiSwapAnimation.Rotate => "swap-rotate",
        UiSwapAnimation.Flip => "swap-flip",
        _ => "",
    };

    internal static string AuraStyle(UiAuraStyle value) => value switch
    {
        UiAuraStyle.Glow => "aura-glow",
        UiAuraStyle.Dual => "aura-dual",
        UiAuraStyle.Holo => "aura-holo",
        UiAuraStyle.Rainbow => "aura-rainbow",
        UiAuraStyle.Gold => "aura-gold",
        UiAuraStyle.Silver => "aura-silver",
        _ => "",
    };

    internal static string AuraSize(UiSize value) => value switch
    {
        UiSize.Xs => "aura-xs",
        UiSize.Sm => "aura-sm",
        UiSize.Md => "aura-md",
        UiSize.Lg => "aura-lg",
        UiSize.Xl => "aura-xl",
        _ => "",
    };

    internal static string Marker(UiMarker value) => value switch
    {
        UiMarker.Arrow => "collapse-arrow",
        UiMarker.Plus => "collapse-plus",
        _ => "",
    };

    internal static string TabsStyle(UiTabStyle value) => value switch
    {
        UiTabStyle.Box => "tabs-box",
        UiTabStyle.Border => "tabs-border",
        UiTabStyle.Lift => "tabs-lift",
        _ => "",
    };

    /// <remarks>
    ///     A row of tabs sits above or below its panel and nowhere else, so the horizontal members of
    ///     <see cref="UiPosition" /> return nothing rather than a class daisyUI never defined.
    /// </remarks>
    internal static string TabsPosition(UiPosition value) => value switch
    {
        UiPosition.Top => "tabs-top",
        UiPosition.Bottom => "tabs-bottom",
        _ => "",
    };

    internal static string MaskShape(UiMaskShape value) => value switch
    {
        UiMaskShape.Squircle => "mask-squircle",
        UiMaskShape.Star => "mask-star",
        UiMaskShape.Star2 => "mask-star-2",
        UiMaskShape.Heart => "mask-heart",
        UiMaskShape.Hexagon => "mask-hexagon",
        UiMaskShape.Hexagon2 => "mask-hexagon-2",
        UiMaskShape.Pentagon => "mask-pentagon",
        UiMaskShape.Decagon => "mask-decagon",
        UiMaskShape.Diamond => "mask-diamond",
        UiMaskShape.Triangle => "mask-triangle",
        UiMaskShape.Triangle2 => "mask-triangle-2",
        UiMaskShape.Triangle3 => "mask-triangle-3",
        UiMaskShape.Triangle4 => "mask-triangle-4",
        UiMaskShape.Half1 => "mask-half-1",
        UiMaskShape.Half2 => "mask-half-2",
        _ => "mask-circle",
    };

    internal static string OtpTone(UiTone value) => value switch
    {
        UiTone.Neutral => "otp-neutral",
        UiTone.Primary => "otp-primary",
        UiTone.Secondary => "otp-secondary",
        UiTone.Accent => "otp-accent",
        UiTone.Info => "otp-info",
        UiTone.Success => "otp-success",
        UiTone.Warning => "otp-warning",
        UiTone.Error => "otp-error",
        _ => "",
    };

    internal static string OtpSize(UiSize value) => value switch
    {
        UiSize.Xs => "otp-xs",
        UiSize.Sm => "otp-sm",
        UiSize.Md => "otp-md",
        UiSize.Lg => "otp-lg",
        UiSize.Xl => "otp-xl",
        _ => "",
    };

    internal static string LoadingShape(UiLoadingShape value) => value switch
    {
        UiLoadingShape.Dots => "loading-dots",
        UiLoadingShape.Ring => "loading-ring",
        UiLoadingShape.Ball => "loading-ball",
        UiLoadingShape.Bars => "loading-bars",
        UiLoadingShape.Infinity => "loading-infinity",
        _ => "loading-spinner",
    };

    internal static string TooltipPosition(UiPosition value) => value switch
    {
        UiPosition.Top => "tooltip-top",
        UiPosition.Bottom => "tooltip-bottom",
        UiPosition.Left => "tooltip-left",
        UiPosition.Right => "tooltip-right",
        _ => "",
    };

    internal static string TooltipAlign(UiAlign value) => value switch
    {
        UiAlign.Start => "tooltip-start",
        UiAlign.Center => "tooltip-center",
        UiAlign.End => "tooltip-end",
        _ => "",
    };

    /// <remarks>
    ///     A drawer opens from the left or the right edge, and daisyUI's one class for it is <c>drawer-end</c>,
    ///     so only <see cref="UiPosition.Right" /> writes anything.
    /// </remarks>
    /// <remarks>daisyUI hides one side of a divider's line with these, so its words sit at that edge.</remarks>
    internal static string DividerAlign(UiAlign value) => value switch
    {
        UiAlign.Start => "divider-start",
        UiAlign.End => "divider-end",
        _ => "",
    };

    /// <remarks>
    ///     The width from which a sidebar sits in the page's flow instead of sliding over it. Every member a complete
    ///     literal: <c>"lg:" + "drawer-open"</c> is invisible to Tailwind's scan, and the sidebar would never open.
    /// </remarks>
    internal static string SidebarInFlowFrom(UiBreakpoint value) => value switch
    {
        UiBreakpoint.Sm => "sm:drawer-open",
        UiBreakpoint.Md => "md:drawer-open",
        UiBreakpoint.Lg => "lg:drawer-open",
        UiBreakpoint.Xl => "xl:drawer-open",
        _ => "lg:drawer-open",
    };

    /// <remarks>
    ///     Where a toast or a stack of them is pinned. SIX complete literals rather than an edge joined to an
    ///     alignment: <c>"top-3 " + side</c> is two names Tailwind can see and one it cannot, and the toast would
    ///     appear in the middle of the screen with the build green.
    ///     <para>
    ///     Every one keeps the side inset on a phone (<c>inset-x-3</c> until <c>sm</c>), because a toast pinned to
    ///     a corner of a 360px screen is a toast with no room to say anything.
    ///     </para>
    /// </remarks>
    internal static string ToastCorner(UiPosition? position, UiAlign? align) =>
        (position, align) switch
        {
            (UiPosition.Top, UiAlign.Start) => "inset-x-3 top-3 sm:inset-x-auto sm:left-3",
            (UiPosition.Top, UiAlign.End) => "inset-x-3 top-3 sm:inset-x-auto sm:right-3",
            (UiPosition.Top, _) => "inset-x-3 top-3 sm:inset-x-0",
            (_, UiAlign.Start) => "inset-x-3 bottom-3 sm:inset-x-auto sm:left-3",
            (_, UiAlign.End) => "inset-x-3 bottom-3 sm:inset-x-auto sm:right-3",
            _ => "inset-x-3 bottom-3 sm:inset-x-0",
        };

    /// <remarks>
    ///     Tailwind's own resize utilities, one complete literal per member — <c>"resize-" + value</c> is invisible
    ///     to the scan, and a textarea that asked for a fixed size would silently keep its handle.
    /// </remarks>
    internal static string Resize(UiResize value) => value switch
    {
        UiResize.Horizontal => "resize-x",
        UiResize.Both => "resize",
        UiResize.None => "resize-none",
        _ => "resize-y",
    };

    /// <remarks>The toggle is only needed while the sidebar slides over the page, so it hides where the sidebar docks.</remarks>
    internal static string HiddenFrom(UiBreakpoint value) => value switch
    {
        UiBreakpoint.Sm => "sm:hidden",
        UiBreakpoint.Md => "md:hidden",
        UiBreakpoint.Lg => "lg:hidden",
        UiBreakpoint.Xl => "xl:hidden",
        _ => "lg:hidden",
    };

    /// <remarks>
    ///     The mirror of <see cref="HiddenFrom" />, for the collapse control: narrowing the sidebar to a rail only
    ///     means anything once it is DOCKED, so the control is hidden until then. ONE class name, as every member
    ///     here is — the call site pairs it with its own <c>hidden</c>, because a member returning two names would
    ///     be a name built by concatenation in everything but spelling, which is what this file exists to avoid.
    /// </remarks>
    internal static string ShownFrom(UiBreakpoint value) => value switch
    {
        UiBreakpoint.Sm => "sm:inline-flex",
        UiBreakpoint.Md => "md:inline-flex",
        UiBreakpoint.Lg => "lg:inline-flex",
        UiBreakpoint.Xl => "xl:inline-flex",
        _ => "lg:inline-flex",
    };

    internal static string SubheadingSize(UiSize value) => value switch
    {
        UiSize.Xs => "text-xs",
        UiSize.Lg => "text-base",
        UiSize.Xl => "text-lg",
        _ => "text-sm",
    };

    internal static string TextSize(UiSize value) => value switch
    {
        UiSize.Xs => "text-xs",
        UiSize.Sm => "text-sm",
        UiSize.Lg => "text-base",
        UiSize.Xl => "text-lg",
        _ => "text-sm",
    };

    /// <remarks>The ink colours, which are what keep body text readable on every theme's base.</remarks>
    internal static string TextTone(UiTone value) => value switch
    {
        UiTone.Primary => "text-primary",
        UiTone.Secondary => "text-secondary",
        UiTone.Accent => "text-accent",
        UiTone.Info => "text-info",
        UiTone.Success => "text-success",
        UiTone.Warning => "text-warning",
        UiTone.Error => "text-error",
        _ => "",
    };

    internal static string DrawerPosition(UiPosition value) => value switch
    {
        UiPosition.Right => "drawer-end",
        _ => "",
    };

    /// <remarks>
    ///     daisyUI's avatar has no size classes of its own — its docs size the inner box with a width
    ///     utility — so the literals live here, where Tailwind can see them.
    /// </remarks>
    internal static string AvatarSize(UiSize value) => value switch
    {
        UiSize.Xs => "w-6",
        UiSize.Sm => "w-8",
        UiSize.Lg => "w-16",
        UiSize.Xl => "w-24",
        _ => "w-10",
    };

    internal static string MegamenuSize(UiSize value) => value switch
    {
        UiSize.Xs => "megamenu-xs",
        UiSize.Sm => "megamenu-sm",
        UiSize.Md => "megamenu-md",
        UiSize.Lg => "megamenu-lg",
        UiSize.Xl => "megamenu-xl",
        _ => "",
    };
}
