namespace Rask.Core;

/// <summary>
///     A handler stored in another shape than the one it was written in, which still answers for the one written:
///     <see cref="DelegateOwner" /> reads <see cref="Inner" /> to find the component that owns it.
/// </summary>
internal interface IHandlerAdapter
{
    /// <summary>The handler as the call site wrote it.</summary>
    Delegate Inner { get; }
}
