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
}
