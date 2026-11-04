namespace Rask;

/// <summary>
///     A kit control the reader can be shown disabled, which writes through handlers of its own — a hidden field
///     the runtime fills, a thumb, a cell, the editor's engine. While it is drawn disabled it writes nothing it is
///     sent (<see cref="UiRefusals.Locked(object, Rask.Core.Forms.ExpressionAccessor.Accessor?)" />, #1272): the
///     browser sends no event for it, so what arrives was written by hand.
/// </summary>
internal interface IUiLockable
{
    /// <summary>Whether the control is drawn disabled now.</summary>
    bool Locked { get; }

    /// <summary>The name it posts under, said in the log when nothing is bound; a control with a <c>Name</c> has it.</summary>
    string? Name => null;
}
