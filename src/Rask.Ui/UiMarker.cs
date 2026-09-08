namespace Rask.Ui;

/// <summary>
/// The sign that a section opens.
/// </summary>
/// <remarks>
/// Worth drawing one. A heading that opens and closes looks exactly like a heading that does not, so
/// without a marker the only way to discover the content is to click something that gave no indication
/// it would do anything.
/// </remarks>
public enum UiMarker
{
    /// <summary>No marker drawn.</summary>
    None = 0,

    /// <summary>A chevron that turns as it opens.</summary>
    Arrow,

    /// <summary>A plus that becomes a minus.</summary>
    Plus,
}
