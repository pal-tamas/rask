namespace Rask.Storage;

/// <summary>Why a file was refused.</summary>
public enum FileRejection
{
    /// <summary>It is larger than <see cref="StorageOptions.MaxFileSize"/>.</summary>
    TooLarge,

    /// <summary>Its content is not one of <see cref="StorageOptions.AllowedTypes"/>.</summary>
    TypeNotAllowed,
}
