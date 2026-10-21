using Microsoft.Extensions.DependencyInjection;

namespace Rask.Core.Printing;

/// <summary>Turns PDF printing on in a RaskApp whose app references Rask.Pdf — with no code in the app.</summary>
/// <remarks>
/// Rask.Server cannot reference Rask.Pdf: every server app would then carry it and fetch its browser at build.
/// So, as with <see cref="Diagnostics.DevTools.RaskDevToolsLoader"/>, the host asks for the bootstrap BY NAME; an
/// app without the package finds no such type and nothing happens. The name is a constant so the trimmer resolves it.
/// </remarks>
internal static class PdfLoader
{
    /// <summary>The bootstrap's assembly-qualified name.</summary>
    internal const string BootstrapTypeName = "Rask.Printing.PdfBootstrap, Rask.Pdf";

    /// <summary>Registers Rask.Pdf on <paramref name="services"/> when the app has it; whether it did.</summary>
    internal static bool TryAttach(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var type = Type.GetType(BootstrapTypeName, throwOnError: false);
        if (type is null || Activator.CreateInstance(type) is not IPdfBootstrap bootstrap)
        {
            return false;
        }

        bootstrap.Attach(services);
        return true;
    }
}
