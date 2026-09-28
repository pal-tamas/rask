namespace Rask.Mailing;

/// <summary>A file attached to an email.</summary>
/// <param name="FileName">The attachment's file name (e.g. <c>invoice.pdf</c>).</param>
/// <param name="ContentType">The MIME content type (e.g. <c>application/pdf</c>).</param>
/// <param name="Content">The attachment bytes.</param>
public sealed record EmailAttachment(string FileName, string ContentType, byte[] Content);
