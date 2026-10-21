using Microsoft.Extensions.DependencyInjection;

namespace Rask.Core.Printing;

/// <summary>What Rask.Pdf registers when a RaskApp finds it (<see cref="PdfLoader"/>).</summary>
internal interface IPdfBootstrap
{
    /// <summary>Registers the printer, as <c>AddRaskPdf()</c> does.</summary>
    void Attach(IServiceCollection services);
}
