using Rask.Core.Forms;

namespace Rask;

/// <summary>Whether a dispatched request is validated here, before its handler runs.</summary>
/// <remarks>
///     <para>
///         On the server, always: it is where a request is trusted, and a form's switch is no say over that.
///         <see cref="RaskValidation.AutoValidate" /> is what a <c>Form</c> reads — set to <see langword="false" />
///         to stop forms validating on their own, it used to stop the server checking every request too, so a
///         command could reach its handler with every rule broken. Turning request validation off on the server is
///         <c>CqrsOptions.ValidateRequests</c> (<c>c.Validation.Off()</c> clears it, and registers no validator).
///     </para>
///     <para>
///         In the browser the form's switch still decides: nothing there is trusted, the server checks again, and
///         an app that turned validation off would otherwise see its requests refused before they travel while its
///         forms say nothing.
///     </para>
/// </remarks>
internal static class RequestValidationSwitch
{
    /// <summary>True on the server; <see cref="RaskValidation.AutoValidate" /> in the browser.</summary>
    internal static bool Validates => !OperatingSystem.IsBrowser() || RaskValidation.AutoValidate;
}
