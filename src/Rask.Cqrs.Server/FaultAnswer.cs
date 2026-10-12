using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rask.Hosting.Shared;

namespace Rask.Cqrs.Server;

// What a caller is told of a handler's exception, decided once when the endpoints are mapped: the
// exception's own text only where the host says it is Development AND the app asked for it, and always a
// reference whose log line carries the exception. A host that names no environment is Production.
internal sealed partial class FaultAnswer
{
    private readonly ILogger _log;

    private FaultAnswer(bool showsDetail, ILogger log)
    {
        ShowsDetail = showsDetail;
        _log = log;
    }

    /// <summary>Whether an exception's text may leave the server in a problem document.</summary>
    internal bool ShowsDetail { get; }

    internal static FaultAnswer For(IServiceProvider services, CqrsServerOptions options)
    {
        var development = services.GetService<IHostEnvironment>()?.IsDevelopment() == true;
        var log = services.GetService<ILoggerFactory>()?.CreateLogger(ErrorReferenceId.Category)
                  ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        if (options.IncludeExceptionDetail && !development)
        {
            DetailIgnored(log);
        }

        return new FaultAnswer(options.IncludeExceptionDetail && development, log);
    }

    /// <summary>Logs <paramref name="thrown" /> at Error and hands back the reference the line carries.</summary>
    internal string Record(Exception thrown)
    {
        var reference = ErrorReferenceId.New();
        Unhandled(_log, thrown, reference);
        return reference;
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Rask:Cqrs:Server:IncludeExceptionDetail is set and is ignored: an exception's text leaves the "
                  + "server only in the Development environment. A 500 carries a reference to the log line instead.")]
    private static partial void DetailIgnored(ILogger log);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "An unhandled exception reached a remote dispatch response. Reference {Reference}")]
    private static partial void Unhandled(ILogger log, Exception thrown, string reference);
}
