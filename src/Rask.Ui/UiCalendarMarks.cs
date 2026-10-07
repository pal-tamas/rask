namespace Rask;

/// <summary>How one day is drawn.</summary>
internal readonly record struct UiCalendarMarks(bool Selected, bool InRange, bool Start, bool End, bool EndPreview);
