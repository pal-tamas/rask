namespace Rask.Storage.Serving;

/// <summary>How a request reached a file, which decides what it may see and how long it may be cached.</summary>
internal enum StoredFileAccess
{
    /// <summary><see cref="IFiles.Download"/>, behind the app's own authorization.</summary>
    Download,

    /// <summary>A temporary URL's token.</summary>
    Temporary,

    /// <summary>The public route, which serves only files saved as public.</summary>
    Public,
}
