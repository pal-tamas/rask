namespace Rask.Storage;

/// <summary>How one file is saved.</summary>
public sealed class SaveOptions
{
    /// <summary>
    /// Whether anyone with the link may fetch the file through <see cref="IFiles.Url"/>. Default <c>false</c>:
    /// a private file is reached only through <see cref="IFiles.TemporaryUrlAsync"/> or
    /// <see cref="IFiles.Download"/>.
    /// </summary>
    public bool Public { get; set; }
}
