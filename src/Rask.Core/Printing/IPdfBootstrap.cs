using Microsoft.Extensions.DependencyInjection;

namespace Rask.Core.Printing;

/// <summary>What Rask.Pdf registers when the server host's <c>AddRask()</c> finds it (<see cref="PdfLoader"/>).</summary>
internal interface IPdfBootstrap
{
    /// <summary>Registers the printer, as <c>AddRaskPdf()</c> does.</summary>
    void Attach(IServiceCollection services);
}
