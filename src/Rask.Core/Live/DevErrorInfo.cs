namespace Rask.Core.Live;

/// <summary>
///     A fault to show the developer <em>over</em> the running app, rather than instead of it.
/// </summary>
/// <param name="Kind">
///     What produced it — <c>"handler"</c> or <c>"lifecycle"</c> for an app fault, <c>"build"</c> for a
///     compile failure reported by <c>rask dev</c>. The client uses it for the overlay's heading, so the
///     reader knows whether to look at their last click or their last save.
/// </param>
/// <param name="Title">The exception type name, or the build tool's summary line.</param>
/// <param name="Message">The exception message, or the first compiler error.</param>
/// <param name="Detail">The stack trace or the full error list. May be long; the overlay scrolls it.</param>
/// <remarks>
///     <para>
///         <b>Development only.</b> It carries a stack trace, so it must never be built on a host that
///         reports Production — the call sites gate on <see cref="LiveOptions.IsDevelopment" />, which
///         since #605 reflects the host's real environment rather than only an environment variable.
///     </para>
///     <para>
///         Deliberately four strings rather than the exception: the payload is JSON on a wire, the client
///         renders text, and anything richer would be inventing a serialization format for exceptions that
///         nothing needs.
///     </para>
/// </remarks>
public sealed record DevErrorInfo(string Kind, string Title, string Message, string Detail)
{
    /// <summary>The overlay's cap on <see cref="Detail" />. A stack that long is already unreadable.</summary>
    private const int MaxDetail = 8 * 1024;

    /// <summary>
    ///     Builds the record from a caught exception. Returns <c>null</c> outside development, so a caller
    ///     cannot leak a stack trace by forgetting the gate.
    /// </summary>
    public static DevErrorInfo? From(Exception ex, string kind)
    {
        ArgumentNullException.ThrowIfNull(ex);

        // The same decision the development error page makes, from the same resolver, so the two can
        // never disagree about whether this host is in development (#605).
        if (!Components.DefaultErrorPage.ResolveIsDevelopment(
                LiveOptions.IsDevelopment,
                Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
                Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")))
        {
            return null;
        }

        // The innermost exception is the one the developer wrote; the outer ones are the framework's
        // call stack getting it here. Show that one and let the detail carry the rest.
        var innermost = ex;
        while (innermost.InnerException is { } inner)
        {
            innermost = inner;
        }

        var detail = ex.ToString();
        if (detail.Length > MaxDetail)
        {
            detail = detail[..MaxDetail] + "\n… (truncated)";
        }

        return new DevErrorInfo(kind, innermost.GetType().Name, innermost.Message, detail);
    }
}
