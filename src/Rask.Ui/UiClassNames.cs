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

    internal static string BadgeTone(Ui.Tone value) => value switch
    {
        Ui.Tone.Neutral => "badge-neutral",
        Ui.Tone.Primary => "badge-primary",
        Ui.Tone.Secondary => "badge-secondary",
        Ui.Tone.Accent => "badge-accent",
        Ui.Tone.Info => "badge-info",
        Ui.Tone.Success => "badge-success",
        Ui.Tone.Warning => "badge-warning",
        Ui.Tone.Error => "badge-error",
        _ => "",
    };

    internal static string BadgeVariant(Ui.Variant value) => value switch
    {
        Ui.Variant.Outline => "badge-outline",
        Ui.Variant.Soft => "badge-soft",
        Ui.Variant.Dash => "badge-dash",
        Ui.Variant.Ghost => "badge-ghost",
        _ => "",
    };

    internal static string BadgeSize(Ui.Size value) => value switch
    {
        Ui.Size.Xs => "badge-xs",
        Ui.Size.Sm => "badge-sm",
        Ui.Size.Md => "badge-md",
        Ui.Size.Lg => "badge-lg",
        Ui.Size.Xl => "badge-xl",
        _ => "",
    };

    internal static string AlertTone(Ui.Tone value) => value switch
    {
        Ui.Tone.Info => "alert-info",
        Ui.Tone.Success => "alert-success",
        Ui.Tone.Warning => "alert-warning",
        Ui.Tone.Error => "alert-error",
        _ => "",
    };

    internal static string AlertVariant(Ui.Variant value) => value switch
    {
        Ui.Variant.Outline => "alert-outline",
        Ui.Variant.Soft => "alert-soft",
        Ui.Variant.Dash => "alert-dash",
        _ => "",
    };

    internal static string InputTone(Ui.Tone value) => value switch
    {
        Ui.Tone.Neutral => "input-neutral",
        Ui.Tone.Primary => "input-primary",
        Ui.Tone.Secondary => "input-secondary",
        Ui.Tone.Accent => "input-accent",
        Ui.Tone.Info => "input-info",
        Ui.Tone.Success => "input-success",
        Ui.Tone.Warning => "input-warning",
        Ui.Tone.Error => "input-error",
        _ => "",
    };

    internal static string InputVariant(Ui.Variant value) => value switch
    {
        Ui.Variant.Ghost => "input-ghost",
        _ => "",
    };

    internal static string InputSize(Ui.Size value) => value switch
    {
        Ui.Size.Xs => "input-xs",
        Ui.Size.Sm => "input-sm",
        Ui.Size.Md => "input-md",
        Ui.Size.Lg => "input-lg",
        Ui.Size.Xl => "input-xl",
        _ => "",
    };

    internal static string SelectTone(Ui.Tone value) => value switch
    {
        Ui.Tone.Neutral => "select-neutral",
        Ui.Tone.Primary => "select-primary",
        Ui.Tone.Secondary => "select-secondary",
        Ui.Tone.Accent => "select-accent",
        Ui.Tone.Info => "select-info",
        Ui.Tone.Success => "select-success",
        Ui.Tone.Warning => "select-warning",
        Ui.Tone.Error => "select-error",
        _ => "",
    };

    internal static string SelectVariant(Ui.Variant value) => value switch
    {
        Ui.Variant.Ghost => "select-ghost",
        _ => "",
    };

    internal static string SelectSize(Ui.Size value) => value switch
    {
        Ui.Size.Xs => "select-xs",
        Ui.Size.Sm => "select-sm",
        Ui.Size.Md => "select-md",
        Ui.Size.Lg => "select-lg",
        Ui.Size.Xl => "select-xl",
        _ => "",
    };

    internal static string TextareaTone(Ui.Tone value) => value switch
    {
        Ui.Tone.Neutral => "textarea-neutral",
        Ui.Tone.Primary => "textarea-primary",
        Ui.Tone.Secondary => "textarea-secondary",
        Ui.Tone.Accent => "textarea-accent",
        Ui.Tone.Info => "textarea-info",
        Ui.Tone.Success => "textarea-success",
        Ui.Tone.Warning => "textarea-warning",
        Ui.Tone.Error => "textarea-error",
        _ => "",
    };

    internal static string TextareaVariant(Ui.Variant value) => value switch
    {
        Ui.Variant.Ghost => "textarea-ghost",
        _ => "",
    };

    internal static string TextareaSize(Ui.Size value) => value switch
    {
        Ui.Size.Xs => "textarea-xs",
        Ui.Size.Sm => "textarea-sm",
        Ui.Size.Md => "textarea-md",
        Ui.Size.Lg => "textarea-lg",
        Ui.Size.Xl => "textarea-xl",
        _ => "",
    };

    // A chart's colours, one table per use. Tailwind's own colour utilities over daisyUI's theme colours, so a series
    // follows the theme; every one a complete literal for the reason this file exists.
    internal static string ChartStroke(Ui.Tone value) => value switch
    {
        Ui.Tone.Neutral => "stroke-neutral",
        Ui.Tone.Primary => "stroke-primary",
        Ui.Tone.Secondary => "stroke-secondary",
        Ui.Tone.Accent => "stroke-accent",
        Ui.Tone.Info => "stroke-info",
        Ui.Tone.Success => "stroke-success",
        Ui.Tone.Warning => "stroke-warning",
        Ui.Tone.Error => "stroke-error",
        _ => "",
    };

    internal static string ChartFill(Ui.Tone value) => value switch
    {
        Ui.Tone.Neutral => "fill-neutral",
        Ui.Tone.Primary => "fill-primary",
        Ui.Tone.Secondary => "fill-secondary",
        Ui.Tone.Accent => "fill-accent",
        Ui.Tone.Info => "fill-info",
        Ui.Tone.Success => "fill-success",
        Ui.Tone.Warning => "fill-warning",
        Ui.Tone.Error => "fill-error",
        _ => "",
    };

    // The area under a line: the line's colour, faint enough that a second area and the grid show through it.
    internal static string ChartArea(Ui.Tone value) => value switch
    {
        Ui.Tone.Neutral => "fill-neutral/15",
        Ui.Tone.Primary => "fill-primary/15",
        Ui.Tone.Secondary => "fill-secondary/15",
        Ui.Tone.Accent => "fill-accent/15",
        Ui.Tone.Info => "fill-info/15",
        Ui.Tone.Success => "fill-success/15",
        Ui.Tone.Warning => "fill-warning/15",
        Ui.Tone.Error => "fill-error/15",
        _ => "",
    };

    // The dot beside a series' name in the legend and a tooltip.
    internal static string ChartSwatch(Ui.Tone value) => value switch
    {
        Ui.Tone.Neutral => "bg-neutral",
        Ui.Tone.Primary => "bg-primary",
        Ui.Tone.Secondary => "bg-secondary",
        Ui.Tone.Accent => "bg-accent",
        Ui.Tone.Info => "bg-info",
        Ui.Tone.Success => "bg-success",
        Ui.Tone.Warning => "bg-warning",
        Ui.Tone.Error => "bg-error",
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

    internal static string CheckboxTone(Ui.Tone value) => value switch
    {
        Ui.Tone.Neutral => "checkbox-neutral",
        Ui.Tone.Primary => "checkbox-primary",
        Ui.Tone.Secondary => "checkbox-secondary",
        Ui.Tone.Accent => "checkbox-accent",
        Ui.Tone.Info => "checkbox-info",
        Ui.Tone.Success => "checkbox-success",
        Ui.Tone.Warning => "checkbox-warning",
        Ui.Tone.Error => "checkbox-error",
        _ => "",
    };

    internal static string CheckboxSize(Ui.Size value) => value switch
    {
        Ui.Size.Xs => "checkbox-xs",
        Ui.Size.Sm => "checkbox-sm",
        Ui.Size.Md => "checkbox-md",
        Ui.Size.Lg => "checkbox-lg",
        Ui.Size.Xl => "checkbox-xl",
        _ => "",
    };

    internal static string RadioTone(Ui.Tone value) => value switch
    {
        Ui.Tone.Neutral => "radio-neutral",
        Ui.Tone.Primary => "radio-primary",
        Ui.Tone.Secondary => "radio-secondary",
        Ui.Tone.Accent => "radio-accent",
        Ui.Tone.Info => "radio-info",
        Ui.Tone.Success => "radio-success",
        Ui.Tone.Warning => "radio-warning",
        Ui.Tone.Error => "radio-error",
        _ => "",
    };

    internal static string RadioSize(Ui.Size value) => value switch
    {
        Ui.Size.Xs => "radio-xs",
        Ui.Size.Sm => "radio-sm",
        Ui.Size.Md => "radio-md",
        Ui.Size.Lg => "radio-lg",
        Ui.Size.Xl => "radio-xl",
        _ => "",
    };

    internal static string ToggleTone(Ui.Tone value) => value switch
    {
        Ui.Tone.Neutral => "toggle-neutral",
        Ui.Tone.Primary => "toggle-primary",
        Ui.Tone.Secondary => "toggle-secondary",
        Ui.Tone.Accent => "toggle-accent",
        Ui.Tone.Info => "toggle-info",
        Ui.Tone.Success => "toggle-success",
        Ui.Tone.Warning => "toggle-warning",
        Ui.Tone.Error => "toggle-error",
        _ => "",
    };

    internal static string ToggleSize(Ui.Size value) => value switch
    {
        Ui.Size.Xs => "toggle-xs",
        Ui.Size.Sm => "toggle-sm",
        Ui.Size.Md => "toggle-md",
        Ui.Size.Lg => "toggle-lg",
        Ui.Size.Xl => "toggle-xl",
        _ => "",
    };

    internal static string RangeTone(Ui.Tone value) => value switch
    {
        Ui.Tone.Neutral => "range-neutral",
        Ui.Tone.Primary => "range-primary",
        Ui.Tone.Secondary => "range-secondary",
        Ui.Tone.Accent => "range-accent",
        Ui.Tone.Info => "range-info",
        Ui.Tone.Success => "range-success",
        Ui.Tone.Warning => "range-warning",
        Ui.Tone.Error => "range-error",
        _ => "",
    };

    internal static string RangeSize(Ui.Size value) => value switch
    {
        Ui.Size.Xs => "range-xs",
        Ui.Size.Sm => "range-sm",
        Ui.Size.Md => "range-md",
        Ui.Size.Lg => "range-lg",
        Ui.Size.Xl => "range-xl",
        _ => "",
    };

    internal static string ProgressTone(Ui.Tone value) => value switch
    {
        Ui.Tone.Neutral => "progress-neutral",
        Ui.Tone.Primary => "progress-primary",
        Ui.Tone.Secondary => "progress-secondary",
        Ui.Tone.Accent => "progress-accent",
        Ui.Tone.Info => "progress-info",
        Ui.Tone.Success => "progress-success",
        Ui.Tone.Warning => "progress-warning",
        Ui.Tone.Error => "progress-error",
        _ => "",
    };

    internal static string LinkTone(Ui.Tone value) => value switch
    {
        Ui.Tone.Neutral => "link-neutral",
        Ui.Tone.Primary => "link-primary",
        Ui.Tone.Secondary => "link-secondary",
        Ui.Tone.Accent => "link-accent",
        Ui.Tone.Info => "link-info",
        Ui.Tone.Success => "link-success",
        Ui.Tone.Warning => "link-warning",
        Ui.Tone.Error => "link-error",
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

    internal static string TooltipTone(Ui.Tone value) => value switch
    {
        Ui.Tone.Primary => "tooltip-primary",
        Ui.Tone.Secondary => "tooltip-secondary",
        Ui.Tone.Accent => "tooltip-accent",
        Ui.Tone.Info => "tooltip-info",
        Ui.Tone.Success => "tooltip-success",
        Ui.Tone.Warning => "tooltip-warning",
        Ui.Tone.Error => "tooltip-error",
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

    internal static string CardSize(Ui.Size value) => value switch
    {
        Ui.Size.Xs => "card-xs",
        Ui.Size.Sm => "card-sm",
        Ui.Size.Md => "card-md",
        Ui.Size.Lg => "card-lg",
        Ui.Size.Xl => "card-xl",
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

    internal static string DividerTone(Ui.Tone value) => value switch
    {
        Ui.Tone.Neutral => "divider-neutral",
        Ui.Tone.Primary => "divider-primary",
        Ui.Tone.Secondary => "divider-secondary",
        Ui.Tone.Accent => "divider-accent",
        Ui.Tone.Info => "divider-info",
        Ui.Tone.Success => "divider-success",
        Ui.Tone.Warning => "divider-warning",
        Ui.Tone.Error => "divider-error",
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

    internal static string DropdownPosition(Ui.Position value) => value switch
    {
        Ui.Position.Top => "dropdown-top",
        Ui.Position.Bottom => "dropdown-bottom",
        Ui.Position.Left => "dropdown-left",
        Ui.Position.Right => "dropdown-right",
        _ => "",
    };

    internal static string DropdownAlign(Ui.Align value) => value switch
    {
        Ui.Align.Start => "dropdown-start",
        Ui.Align.Center => "dropdown-center",
        Ui.Align.End => "dropdown-end",
        _ => "",
    };

    internal static string ModalPosition(Ui.ModalPosition value) => value switch
    {
        Ui.ModalPosition.Top => "modal-top",
        Ui.ModalPosition.Middle => "modal-middle",
        Ui.ModalPosition.Bottom => "modal-bottom",
        Ui.ModalPosition.Start => "modal-start",
        Ui.ModalPosition.End => "modal-end",
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

    internal static string Marker(Ui.Marker value) => value switch
    {
        Ui.Marker.Arrow => "collapse-arrow",
        Ui.Marker.Plus => "collapse-plus",
        _ => "",
    };

    internal static string TabsStyle(Ui.TabStyle value) => value switch
    {
        Ui.TabStyle.Box => "tabs-box",
        Ui.TabStyle.Border => "tabs-border",
        Ui.TabStyle.Lift => "tabs-lift",
        _ => "",
    };

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

    internal static string OtpTone(Ui.Tone value) => value switch
    {
        Ui.Tone.Neutral => "otp-neutral",
        Ui.Tone.Primary => "otp-primary",
        Ui.Tone.Secondary => "otp-secondary",
        Ui.Tone.Accent => "otp-accent",
        Ui.Tone.Info => "otp-info",
        Ui.Tone.Success => "otp-success",
        Ui.Tone.Warning => "otp-warning",
        Ui.Tone.Error => "otp-error",
        _ => "",
    };

    internal static string OtpSize(Ui.Size value) => value switch
    {
        Ui.Size.Xs => "otp-xs",
        Ui.Size.Sm => "otp-sm",
        Ui.Size.Md => "otp-md",
        Ui.Size.Lg => "otp-lg",
        Ui.Size.Xl => "otp-xl",
        _ => "",
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

    internal static string TooltipPosition(Ui.Position value) => value switch
    {
        Ui.Position.Top => "tooltip-top",
        Ui.Position.Bottom => "tooltip-bottom",
        Ui.Position.Left => "tooltip-left",
        Ui.Position.Right => "tooltip-right",
        _ => "",
    };

    internal static string TooltipAlign(Ui.Align value) => value switch
    {
        Ui.Align.Start => "tooltip-start",
        Ui.Align.Center => "tooltip-center",
        Ui.Align.End => "tooltip-end",
        _ => "",
    };

    /// <remarks>
    ///     A drawer opens from the left or the right edge, and daisyUI's one class for it is <c>drawer-end</c>,
    ///     so only <see cref="Ui.Position.Right" /> writes anything.
    /// </remarks>
    /// <remarks>daisyUI hides one side of a divider's line with these, so its words sit at that edge.</remarks>
    internal static string DividerAlign(Ui.Align value) => value switch
    {
        Ui.Align.Start => "divider-start",
        Ui.Align.End => "divider-end",
        _ => "",
    };

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

    /// <remarks>
    ///     Where a toast or a stack of them is pinned. SIX complete literals rather than an edge joined to an
    ///     alignment: <c>"top-3 " + side</c> is two names Tailwind can see and one it cannot, and the toast would
    ///     appear in the middle of the screen with the build green.
    ///     <para>
    ///     Every one keeps the side inset on a phone (<c>inset-x-3</c> until <c>sm</c>), because a toast pinned to
    ///     a corner of a 360px screen is a toast with no room to say anything.
    ///     </para>
    /// </remarks>
    internal static string ToastCorner(Ui.Position? position, Ui.Align? align) =>
        (position, align) switch
        {
            (Ui.Position.Top, Ui.Align.Start) => "inset-x-3 top-3 sm:inset-x-auto sm:left-3",
            (Ui.Position.Top, Ui.Align.End) => "inset-x-3 top-3 sm:inset-x-auto sm:right-3",
            (Ui.Position.Top, _) => "inset-x-3 top-3 sm:inset-x-0",
            (_, Ui.Align.Start) => "inset-x-3 bottom-3 sm:inset-x-auto sm:left-3",
            (_, Ui.Align.End) => "inset-x-3 bottom-3 sm:inset-x-auto sm:right-3",
            _ => "inset-x-3 bottom-3 sm:inset-x-0",
        };

    /// <remarks>
    ///     Tailwind's own resize utilities, one complete literal per member — <c>"resize-" + value</c> is invisible
    ///     to the scan, and a textarea that asked for a fixed size would silently keep its handle.
    /// </remarks>
    internal static string Resize(Ui.Resize value) => value switch
    {
        Ui.Resize.Horizontal => "resize-x",
        Ui.Resize.Both => "resize",
        Ui.Resize.None => "resize-none",
        _ => "resize-y",
    };

    /// <remarks>The toggle is only needed while the sidebar slides over the page, so it hides where the sidebar docks.</remarks>
    internal static string HiddenFrom(Ui.Breakpoint value) => value switch
    {
        Ui.Breakpoint.Sm => "sm:hidden",
        Ui.Breakpoint.Md => "md:hidden",
        Ui.Breakpoint.Lg => "lg:hidden",
        Ui.Breakpoint.Xl => "xl:hidden",
        _ => "lg:hidden",
    };

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

    internal static string SubheadingSize(Ui.Size value) => value switch
    {
        Ui.Size.Xs => "text-xs",
        Ui.Size.Lg => "text-base",
        Ui.Size.Xl => "text-lg",
        _ => "text-sm",
    };

    internal static string TextSize(Ui.Size value) => value switch
    {
        Ui.Size.Xs => "text-xs",
        Ui.Size.Sm => "text-sm",
        Ui.Size.Lg => "text-base",
        Ui.Size.Xl => "text-lg",
        _ => "text-sm",
    };

    /// <remarks>The ink colours, which are what keep body text readable on every theme's base.</remarks>
    internal static string TextTone(Ui.Tone value) => value switch
    {
        Ui.Tone.Primary => "text-primary",
        Ui.Tone.Secondary => "text-secondary",
        Ui.Tone.Accent => "text-accent",
        Ui.Tone.Info => "text-info",
        Ui.Tone.Success => "text-success",
        Ui.Tone.Warning => "text-warning",
        Ui.Tone.Error => "text-error",
        _ => "",
    };

    internal static string DrawerPosition(Ui.Position value) => value switch
    {
        Ui.Position.Right => "drawer-end",
        _ => "",
    };

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

    internal static string MegamenuSize(Ui.Size value) => value switch
    {
        Ui.Size.Xs => "megamenu-xs",
        Ui.Size.Sm => "megamenu-sm",
        Ui.Size.Md => "megamenu-md",
        Ui.Size.Lg => "megamenu-lg",
        Ui.Size.Xl => "megamenu-xl",
        _ => "",
    };
}
