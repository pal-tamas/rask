using Microsoft.Extensions.Logging;

namespace Rask.Blazor;

// Source-generated rather than LogWarning: no argument array is built when the level is off (CA1848/CA1873).
internal static partial class BlazorLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message =
        "Blazor island {Island}: its on{Event} handler takes {ArgsType}, which Rask cannot build from the "
        + "browser's event, so the handler is not wired. Take one of Microsoft.AspNetCore.Components.Web's "
        + "event args, or none.")]
    public static partial void UnmappedEventArgs(ILogger logger, string island, string @event, string argsType);
}
