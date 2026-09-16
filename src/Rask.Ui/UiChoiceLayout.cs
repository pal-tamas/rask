namespace Rask.Ui;

/// <summary>
/// How a list of choices is laid out — Flux UI's variants for a radio or checkbox group.
/// </summary>
/// <remarks>
/// It is <c>Layout</c> rather than <c>Variant</c> on the controls that take it, because
/// <see cref="UiFormField{T}" /> already carries <see cref="UiVariant" /> — Solid, Outline, Ghost and the rest —
/// and two properties called Variant meaning different things on one control is worse than one with a plainer
/// name. This says where the choices SIT; that one says how a control is painted.
/// </remarks>
public enum UiChoiceLayout
{
    /// <summary>One choice per row, the control beside its words. The default, and what a long list wants.</summary>
    List,

    /// <summary>Each choice a bordered card, with room for a description under its label.</summary>
    Cards,

    /// <summary>Each choice a rounded pill, wrapping onto as many rows as it needs.</summary>
    Pills,

    /// <summary>Each choice a button, wrapping as pills do but squared off.</summary>
    Buttons,

    /// <summary>One joined strip of buttons — for a few short choices that read as one control.</summary>
    Segmented,
}
