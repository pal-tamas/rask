using System;

namespace Rask.Core.Dom.Build;

// The snapshot cannot become element types; RaskDomEmit reports it as RASKDOM001.
public sealed class DomEmitException : Exception
{
    public DomEmitException()
    {
    }

    public DomEmitException(string message)
        : base(message)
    {
    }

    public DomEmitException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
