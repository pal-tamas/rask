using System;

namespace Rask.External.Tasks;

/// <summary>A combination of islands no build input can be written for, with the code it is reported under.</summary>
/// <remarks>
///     Thrown by <see cref="ExternalBuildPlan" />, which is pure and has no log; the task that called it logs
///     the message as a coded build error rather than letting it surface as MSB4018 and a stack trace.
/// </remarks>
internal sealed class ExternalBuildException(string code, string message) : InvalidOperationException(message)
{
    /// <summary>The <c>RASKISLAND</c> code — one of <see cref="ExternalDiagnosticCodes" />.</summary>
    public string Code { get; } = code;
}
