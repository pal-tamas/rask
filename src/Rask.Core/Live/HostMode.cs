namespace Rask.Core.Live;

/// <summary>
///     Whether the host that owns the work in progress runs in Development: one per host, in its services.
/// </summary>
/// <remarks>
///     It decides whether a person is shown an exception — in a toast, an error page, the overlay — so it belongs
///     to the HOST, not to the process. A process-wide flag, set by whichever host started first, once showed a
///     Production host's visitors a Development host's exception text (and hid the overlay from a Development
///     host started second). A host registers one (<c>AddRask</c>, the WebAssembly host); what runs on its behalf
///     — a render, a handler, a session's reply — asks the services in scope. Only where no host registered one
///     does <see cref="LiveOptions.IsDevelopment" /> answer, which no host writes.
/// </remarks>
internal sealed class HostMode
{
    private static readonly HostMode Development = new(isDevelopment: true);
    private static readonly HostMode Production = new(isDevelopment: false);
    private static readonly HostMode Unknown = new(isDevelopment: null);

    private readonly bool? _isDevelopment;

    private HostMode(bool? isDevelopment) => _isDevelopment = isDevelopment;

    /// <summary>A host's mode: null for one that cannot tell — a container that is not a host, which has no environment.</summary>
    internal static HostMode For(bool? isDevelopment) => isDevelopment switch
    {
        true => Development,
        false => Production,
        null => Unknown,
    };

    /// <summary>The answer of the host whose <paramref name="services" /> these are, or null when it gave none.</summary>
    internal static bool? Of(IServiceProvider? services) =>
        (services?.GetService(typeof(HostMode)) as HostMode)?._isDevelopment ?? LiveOptions.IsDevelopment;

    /// <summary>The answer of the host running the work in progress, or null when there is none to give one.</summary>
    internal static bool? InScope => Of(Ambient.Services ?? AmbientServices.Current);

    /// <summary>Whether the work in progress runs on a Development host; unknown is not Development.</summary>
    internal static bool IsDevelopmentInScope => InScope == true;

    /// <summary>
    ///     As <see cref="InScope" />, with the standard environment variables as the last word for a component rendered
    ///     outside any host — what the error page and the overlay decide by (#605).
    /// </summary>
    internal static bool ResolvedInScope =>
        Components.DefaultErrorPage.ResolveIsDevelopment(
            InScope,
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
            Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"));
}
