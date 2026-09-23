namespace Rask;

/// <summary>
///     A form's validation feedback: <c>Validation.Message</c> for one field, <c>Validation.Summary</c> for the whole
///     form, <c>Validation.Indicator</c> while an asynchronous rule is still running.
/// </summary>
/// <remarks>
///     Grouped so typing <c>Validation.</c> lists them, and in the <c>Rask</c> namespace so <c>using Rask;</c> reaches
///     them — including from inside a <c>Rask.*</c> namespace, which no <c>Rask.Validation</c> namespace shadows.
/// </remarks>
public static partial class Validation;
