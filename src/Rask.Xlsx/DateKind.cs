namespace Rask.Workbooks;

/// <summary>What a number format makes of a serial: nothing (a number), a day, or a moment (it shows a time).</summary>
internal enum DateKind
{
    None,
    Day,
    Moment,
}
