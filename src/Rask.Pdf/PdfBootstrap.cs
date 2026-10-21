using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Printing;

namespace Rask.Printing;

/// <summary>Created by name by a RaskApp that finds Rask.Pdf beside it (Core's <c>PdfLoader</c>).</summary>
internal sealed class PdfBootstrap : IPdfBootstrap
{
    public void Attach(IServiceCollection services) => services.AddRaskPdf();
}
