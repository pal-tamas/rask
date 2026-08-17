namespace Rask.Core.Forms;

/// <summary>
///     One validation message together with the field it belongs to, as returned by
///     <see cref="EditContext.GetValidationEntries" />.
/// </summary>
/// <param name="Field">The name of the field the message is about.</param>
/// <param name="Message">The message, written for the person reading it.</param>
public readonly record struct ValidationEntry(string Field, string Message);
