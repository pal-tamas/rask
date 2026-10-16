namespace Rask;

/// <summary>
///     A kit control that can be drawn as a button and forwards the attributes it is given to it — what a
///     <see cref="UiModalTrigger" /> writes its invoker command on when the button is not an element of its own.
/// </summary>
internal interface IUiPressable
{
    /// <summary>The attributes the button is drawn with, as the control's own <c>Attributes</c>.</summary>
    IReadOnlyDictionary<string, string?>? Attributes { get; set; }
}
