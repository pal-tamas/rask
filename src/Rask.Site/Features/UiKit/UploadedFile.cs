namespace Rask.Site.Features.UiKit;

/// <summary>What the file-upload demo keeps of a file once its handler has returned.</summary>
/// <param name="Name">The name the browser reported.</param>
/// <param name="Size">The size in bytes the browser reported.</param>
/// <param name="Image">The address of a preview, for a file that has one.</param>
internal sealed record UploadedFile(string Name, long Size, string? Image = null);
